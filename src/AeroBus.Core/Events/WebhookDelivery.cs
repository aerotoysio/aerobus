using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AeroBus.Core.Events
{
    /// <summary>HMAC-SHA256 helper shared by delivery and any verification.</summary>
    public static class WebhookSignature
    {
        /// <summary>
        /// <c>sha256=&lt;lowercase-hex&gt;</c> of the HMAC-SHA256 of
        /// <paramref name="body"/> (UTF-8) keyed by <paramref name="secret"/>.
        /// This is the exact value put in the <c>X-AeroBus-Signature</c> header, so
        /// a receiver reproduces it over the raw request body to verify.
        /// </summary>
        public static string Compute(string secret, string body)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    public interface IWebhookDelivery
    {
        /// <summary>POST the event to one subscription. True on a 2xx response.</summary>
        Task<bool> DeliverAsync(WebhookSubscription sub, EventEnvelope evt, CancellationToken ct = default);
    }

    /// <summary>
    /// Signs and POSTs an event envelope to a subscriber's URL. The signature is
    /// computed over the <em>exact</em> serialized body that is sent, so a receiver
    /// can recompute the HMAC over the raw bytes it received.
    /// </summary>
    public sealed class WebhookDelivery : IWebhookDelivery
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        private readonly ILogger<WebhookDelivery> _log;

        public WebhookDelivery(HttpClient http, ILogger<WebhookDelivery> log)
        {
            _http = http;
            _log = log;
        }

        public async Task<bool> DeliverAsync(WebhookSubscription sub, EventEnvelope evt, CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(evt, Json);
            var signature = WebhookSignature.Compute(sub.Secret ?? "", body);

            using var req = new HttpRequestMessage(HttpMethod.Post, sub.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-AeroBus-Signature", signature);
            req.Headers.TryAddWithoutValidation("X-AeroBus-Event", evt.Type);
            req.Headers.TryAddWithoutValidation("X-AeroBus-Delivery", evt.Id.ToString());

            try
            {
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) return true;
                _log.LogWarning(
                    "Webhook delivery of {Type} ({EventId}) to {Url} returned {Status}.",
                    evt.Type, evt.Id, sub.Url, (int)resp.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Webhook delivery of {Type} ({EventId}) to {Url} failed.",
                    evt.Type, evt.Id, sub.Url);
                return false;
            }
        }
    }
}
