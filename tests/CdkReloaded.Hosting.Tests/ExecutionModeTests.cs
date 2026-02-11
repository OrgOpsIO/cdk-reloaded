using CdkReloaded.Hosting;

namespace CdkReloaded.Hosting.Tests;

public class ExecutionModeTests
{
    [Fact]
    public void DetectMode_DefaultsToLocal()
    {
        var mode = CloudApplicationBuilder.DetectMode([]);

        Assert.Equal(ExecutionMode.Local, mode);
    }

    [Fact]
    public void DetectMode_DetectsDeployFromArgs()
    {
        var mode = CloudApplicationBuilder.DetectMode(["deploy"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
    }

    [Fact]
    public void DetectMode_DetectsDeployAmongOtherArgs()
    {
        var mode = CloudApplicationBuilder.DetectMode(["--verbose", "deploy"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
    }

    [Fact]
    public void DetectMode_LocalWhenNoSpecialArgs()
    {
        var mode = CloudApplicationBuilder.DetectMode(["--urls", "http://localhost:5000"]);

        Assert.Equal(ExecutionMode.Local, mode);
    }
}

public class CliCommandTests
{
    [Fact]
    public void DetectModeAndCommand_DefaultsToNone()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand([]);

        Assert.Equal(ExecutionMode.Local, mode);
        Assert.Equal(CliCommand.None, command);
    }

    [Fact]
    public void DetectModeAndCommand_Deploy()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["deploy"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
        Assert.Equal(CliCommand.Deploy, command);
    }

    [Fact]
    public void DetectModeAndCommand_Synth()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["synth"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
        Assert.Equal(CliCommand.Synth, command);
    }

    [Fact]
    public void DetectModeAndCommand_Destroy()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["destroy"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
        Assert.Equal(CliCommand.Destroy, command);
    }

    [Fact]
    public void DetectModeAndCommand_Diff()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["diff"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
        Assert.Equal(CliCommand.Diff, command);
    }

    [Fact]
    public void DetectModeAndCommand_List()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["list"]);

        Assert.Equal(ExecutionMode.Local, mode);
        Assert.Equal(CliCommand.List, command);
    }

    [Fact]
    public void DetectModeAndCommand_CommandAmongOtherArgs()
    {
        var (mode, command) = CloudApplicationBuilder.DetectModeAndCommand(["--verbose", "synth"]);

        Assert.Equal(ExecutionMode.Deploy, mode);
        Assert.Equal(CliCommand.Synth, command);
    }
}
