using System.Diagnostics;
using System.Reflection;
using CdkReloaded.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CdkReloaded.Cdk;

public sealed class CdkDeployRuntime : IRuntime
{
    public async Task RunAsync(
        CloudApplicationContext context,
        IReadOnlyList<Action<IServiceCollection>> serviceConfigurators,
        CancellationToken ct)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger<CdkDeployRuntime>();

        CheckPrerequisites(logger);

        var projectDir = FindProjectDirectory();
        var cdkOutDir = Path.Combine(projectDir, "cdk.out");

        switch (context.Command)
        {
            case CliCommand.Synth:
                await RunSynth(context, projectDir, cdkOutDir, logger, ct);
                break;

            case CliCommand.Destroy:
                await RunSynth(context, projectDir, cdkOutDir, logger, ct);
                await RunDestroy(projectDir, cdkOutDir, logger, ct);
                break;

            case CliCommand.Diff:
                await RunSynth(context, projectDir, cdkOutDir, logger, ct);
                await RunDiff(projectDir, cdkOutDir, logger, ct);
                break;

            case CliCommand.Deploy:
            default:
                await RunDeploy(context, projectDir, cdkOutDir, logger, ct);
                break;
        }
    }

    private static void CheckPrerequisites(ILogger logger)
    {
        CheckTool("dotnet", "--version", "dotnet CLI", logger);
        CheckTool("npx", "--version", "npx (Node.js)", logger);
    }

    private static void CheckTool(string fileName, string arguments, string displayName, ILogger logger)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
                throw new DeploymentException("prerequisites", $"{displayName} is not working correctly.");
        }
        catch (DeploymentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeploymentException("prerequisites",
                $"{displayName} is required but not found. Please install it.", ex);
        }
    }

    private static async Task RunDeploy(
        CloudApplicationContext context, string projectDir, string cdkOutDir,
        ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("=== CdkReloaded Deploy ===");

        logger.LogInformation("[1/3] Publishing for AWS Lambda (linux-arm64)...");
        var publishDir = await PublishForLambda(projectDir, logger, ct);
        logger.LogInformation("Published to: {PublishDir}", publishDir);

        logger.LogInformation("[2/3] Synthesizing CDK stack...");
        SynthCdkStack(context, publishDir, cdkOutDir);
        logger.LogInformation("CloudFormation template generated in {CdkOutDir}", cdkOutDir);

        logger.LogInformation("[3/3] Deploying to AWS...");
        await RunCdkCommand(projectDir, cdkOutDir,
            "deploy --require-approval never --outputs-file cdk-outputs.json", logger, ct);

        // Print outputs if available
        var outputsPath = Path.Combine(projectDir, "cdk-outputs.json");
        if (File.Exists(outputsPath))
        {
            logger.LogInformation("Stack outputs:");
            var outputs = await File.ReadAllTextAsync(outputsPath, ct);
            Console.WriteLine(outputs);
        }

        logger.LogInformation("=== Deployment complete! ===");
    }

    private static async Task RunSynth(
        CloudApplicationContext context, string projectDir, string cdkOutDir,
        ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("[1/2] Publishing for AWS Lambda (linux-arm64)...");
        var publishDir = await PublishForLambda(projectDir, logger, ct);
        logger.LogInformation("Published to: {PublishDir}", publishDir);

        logger.LogInformation("[2/2] Synthesizing CDK stack...");
        SynthCdkStack(context, publishDir, cdkOutDir);
        logger.LogInformation("CloudFormation template generated in {CdkOutDir}", cdkOutDir);
    }

    private static async Task RunDestroy(
        string projectDir, string cdkOutDir, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Destroying stack...");
        await RunCdkCommand(projectDir, cdkOutDir, "destroy --force", logger, ct);
        logger.LogInformation("=== Stack destroyed! ===");
    }

    private static async Task RunDiff(
        string projectDir, string cdkOutDir, ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Comparing with deployed stack...");
        await RunCdkCommand(projectDir, cdkOutDir, "diff", logger, ct);
    }

    private static void SynthCdkStack(CloudApplicationContext context, string publishDir, string cdkOutDir)
    {
        var generator = new CdkStackGenerator(context, publishDir, cdkOutDir);
        var app = generator.Generate();
        app.Synth();
    }

    private static async Task<string> PublishForLambda(string projectDir, ILogger logger, CancellationToken ct)
    {
        var publishDir = Path.Combine(projectDir, "bin", "lambda-publish");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{projectDir}\" " +
                    "-c Release " +
                    "-r linux-arm64 " +
                    "--self-contained true " +
                    $"-o \"{publishDir}\" " +
                    "/p:GenerateRuntimeConfigurationFiles=true " +
                    "/p:InvariantGlobalization=true",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new DeploymentException("publish",
                $"dotnet publish failed (exit code {process.ExitCode}):\n{stderr}\n{stdout}");
        }

        // Rename the entry assembly executable to 'bootstrap' for Lambda custom runtime
        var entryAssembly = Assembly.GetEntryAssembly()!;
        var exeName = entryAssembly.GetName().Name!;
        var sourceExe = Path.Combine(publishDir, exeName);
        var bootstrapPath = Path.Combine(publishDir, "bootstrap");

        if (File.Exists(sourceExe) && !File.Exists(bootstrapPath))
        {
            File.Move(sourceExe, bootstrapPath);
        }

        return publishDir;
    }

    private static async Task RunCdkCommand(
        string projectDir, string cdkOutDir, string cdkArgs,
        ILogger logger, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "npx",
                Arguments = $"cdk {cdkArgs} --app \"{cdkOutDir}\"",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // Stream output in real-time
        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
                Console.WriteLine("      " + line);
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                Console.WriteLine("      " + line);
        }, ct);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var commandName = cdkArgs.Split(' ')[0];
            throw new DeploymentException(commandName,
                $"CDK {commandName} failed (exit code {process.ExitCode})");
        }
    }

    private static string FindProjectDirectory()
    {
        // Walk up from the entry assembly location to find the .csproj
        var entryAssembly = Assembly.GetEntryAssembly()!;
        var assemblyDir = Path.GetDirectoryName(entryAssembly.Location)!;

        // Try to find the project directory from the current working directory first
        var cwd = Directory.GetCurrentDirectory();
        if (Directory.GetFiles(cwd, "*.csproj").Length > 0)
            return cwd;

        // Walk up from the assembly location
        var dir = assemblyDir;
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return cwd;
    }
}
