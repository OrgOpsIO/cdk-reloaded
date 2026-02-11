using CdkReloaded.Hosting;
using CdkReloaded.Runtime.Lambda;

[assembly: RuntimeProvider(typeof(LambdaRuntime), ExecutionMode.Lambda)]
