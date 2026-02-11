using System.Runtime.CompilerServices;
using CdkReloaded.Hosting;
using CdkReloaded.Runtime.Local;

[assembly: InternalsVisibleTo("CdkReloaded.Runtime.Local.Tests")]
[assembly: RuntimeProvider(typeof(LocalRuntime), ExecutionMode.Local)]
