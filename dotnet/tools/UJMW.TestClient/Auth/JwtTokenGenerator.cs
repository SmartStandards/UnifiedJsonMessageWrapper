using Jose;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Security.Cryptography;
using System.Text;
using UJMW.TestClient.Model;

namespace UJMW.TestClient.Auth {

  /// <summary>
  /// Creates signed JWTs via jose-jwt based on the persisted generator settings.
  /// </summary>
  internal static class JwtTokenGenerator {

    /// <summary>
    /// Creates a token. Supported algorithms: none, HS*, RS*, PS* (PEM RSA key) and ES* (PEM EC key).
    /// </summary>
    public static string Generate(JwtGeneratorSettings settings) {
      JwsAlgorithm algorithm;
      if (!Enum.TryParse<JwsAlgorithm>(settings.Algorithm, true, out algorithm)) {
        throw new InvalidOperationException($"Unsupported algorithm '{settings.Algorithm}'.");
      }

      string payloadText = settings.PayloadJson;
      if (string.IsNullOrWhiteSpace(payloadText)) {
        payloadText = "{}";
      }
      JObject payload;
      try {
        payload = JObject.Parse(payloadText);
      } catch (JsonReaderException ex) {
        throw new InvalidOperationException("The payload is no valid JSON object: " + ex.Message);
      }

      if (settings.AddTimestamps) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        payload["iat"] = now;
        payload["nbf"] = now;
        payload["exp"] = now + ((long)settings.LifetimeMinutes * 60);
      }

      string payloadJson = payload.ToString(Formatting.None);
      string algorithmName = algorithm.ToString();

      if (algorithm == JwsAlgorithm.none) {
        return JWT.Encode(payloadJson, null, algorithm);
      }
      if (algorithmName.StartsWith("HS", StringComparison.Ordinal)) {
        return JWT.Encode(payloadJson, GetSecretBytes(settings), algorithm);
      }
      if (algorithmName.StartsWith("RS", StringComparison.Ordinal) || algorithmName.StartsWith("PS", StringComparison.Ordinal)) {
        using (RSA rsa = RSA.Create()) {
          rsa.ImportFromPem(settings.SignKey);
          return JWT.Encode(payloadJson, rsa, algorithm);
        }
      }
      if (algorithmName.StartsWith("ES", StringComparison.Ordinal)) {
        using (ECDsa ecdsa = ECDsa.Create()) {
          ecdsa.ImportFromPem(settings.SignKey);
          return JWT.Encode(payloadJson, ecdsa, algorithm);
        }
      }
      throw new InvalidOperationException($"Unsupported algorithm '{settings.Algorithm}'.");
    }

    /// <summary>
    /// Returns the HMAC secret (UTF8 text or base64 decoded).
    /// </summary>
    private static byte[] GetSecretBytes(JwtGeneratorSettings settings) {
      if (string.IsNullOrEmpty(settings.SignKey)) {
        throw new InvalidOperationException("A sign key is required for HMAC algorithms.");
      }
      if (settings.SignKeyIsBase64) {
        return Convert.FromBase64String(settings.SignKey.Trim());
      }
      return Encoding.UTF8.GetBytes(settings.SignKey);
    }

  }

}
