namespace CdkReloaded.Hosting;

public class CloudDefaults
{
    public LambdaDefaults Lambda { get; } = new();
    public DynamoDbDefaults DynamoDb { get; } = new();
}

public class LambdaDefaults
{
    public int MemoryMb { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 30;
    public string Runtime { get; set; } = "dotnet10";
}

public class DynamoDbDefaults
{
    public string BillingMode { get; set; } = "PayPerRequest";
}
