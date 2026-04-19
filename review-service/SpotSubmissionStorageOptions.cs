public class SpotSubmissionStorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = "submissions/";
    public string? PublicBaseUrl { get; set; }
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];
}
