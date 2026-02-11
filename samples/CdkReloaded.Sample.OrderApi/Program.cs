using CdkReloaded.Hosting;

var builder = CloudApplication.CreateBuilder(args);

builder.AddFunctions().FromAssembly();
builder.AddTables().FromAssembly();

builder.ConfigureDefaults(d => d.Lambda.MemoryMb = 512);

var app = builder.Build();
app.Run();
