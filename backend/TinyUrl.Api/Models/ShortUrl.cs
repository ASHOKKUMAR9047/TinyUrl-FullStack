using System;
using System.ComponentModel.DataAnnotations;

namespace TinyUrl.Api.Models
{
    public class ShortUrl
    {
        [Key]
        [Required]
        [StringLength(6)]
        public string Code { get; set; } = string.Empty;

        [Required]
        public string ShortURL { get; set; } = string.Empty;

        [Required]
        [Url]
        public string OriginalURL { get; set; } = string.Empty;

        public int TotalClicks { get; set; } = 0;

        public bool IsPrivate { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
