namespace ShipStation.Application.Constants;

public static class ShipStationConstants
{
    public const string ApiClient = "shipstation";

    /// <summary>Documented ceiling: 40 requests per minute, per account.</summary>
    public const int RequestsPerMinute = 40;

    public const string RateLimitLimitHeader = "X-Rate-Limit-Limit";
    public const string RateLimitRemainingHeader = "X-Rate-Limit-Remaining";
    public const string RateLimitResetHeader = "X-Rate-Limit-Reset";

    public static class Endpoints
    {
        public const string Orders = "orders";
        public const string CreateOrder = "orders/createorder";
    }
}
