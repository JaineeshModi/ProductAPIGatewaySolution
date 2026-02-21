namespace GatewayApi.Models
{
    public class ProductV2 : Product
    {
        // new optional field
        public List<string>? Tags { get; set; }  // Additive, backward compatible
    }
}
