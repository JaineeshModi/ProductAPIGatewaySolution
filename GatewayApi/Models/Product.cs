using System.ComponentModel.DataAnnotations;

namespace GatewayApi.Models
{
    public class Product
    {
        [Required]
        public string Id { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int Stock { get; set; } // From Warehouse system

    }
}