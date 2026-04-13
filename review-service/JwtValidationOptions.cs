public class JwtValidationOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; set; } =
        "https://cognito-idp.ap-southeast-1.amazonaws.com/ap-southeast-1_5KbPo5kdU";

    public List<string> AllowedClientIds { get; set; } =
    [
        "47d5aql1gg87e093dfoqv8tbqs"
    ];

    public string? SigningKey { get; set; }
}
