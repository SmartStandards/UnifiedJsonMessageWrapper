using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UJMW.TestClient.Inspection;

namespace UJMW.TestClient.Invocation {

  /// <summary>
  /// Result of an UJMW invocation which is displayed in the result box.
  /// </summary>
  public sealed class InvocationResult {

    public const string _KindSuccess = "Success";
    public const string _KindFault = "Fault";
    public const string _KindError = "Error";

    private string _Kind = _KindSuccess;
    private string _Text = string.Empty;

    /// <summary>
    /// 'Success', 'Fault' (UJMW fault property was set) or 'Error' (HTTP/transport/protocol failure).
    /// </summary>
    public string Kind {
      get {
        return _Kind;
      }
      set {
        _Kind = value;
      }
    }

    public string Text {
      get {
        return _Text;
      }
      set {
        _Text = value;
      }
    }

    /// <summary>
    /// Creates a new result.
    /// </summary>
    public static InvocationResult Create(string kind, string text) {
      InvocationResult result = new InvocationResult();
      result.Kind = kind;
      result.Text = text;
      return result;
    }

  }

  /// <summary>
  /// Executes UJMW calls via plain HTTP (wire protocol according to the 'ujmw' skill).
  /// </summary>
  internal static class UjmwInvoker {

    private static readonly HttpClient _HttpClient = new HttpClient();

    /// <summary>
    /// Replaces the {AUTO} token by the sidechannel content and posts the request to '&lt;baseUrl&gt;/&lt;methodName&gt;'.
    /// Never throws for expected failures - they are reported as result of kind 'Error'.
    /// </summary>
    public static InvocationResult Invoke(string baseUrl, string methodName, string authHeader, string sideChannelText, string requestText) {
      string requestBody;
      string preparationError;
      if (!TryPrepareRequestBody(requestText, sideChannelText, out requestBody, out preparationError)) {
        return InvocationResult.Create(InvocationResult._KindError, preparationError);
      }
      string url = CombineUrl(baseUrl, methodName);
      Stopwatch stopwatch = Stopwatch.StartNew();
      try {
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)) {
          request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
          if (!string.IsNullOrWhiteSpace(authHeader)) {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader.Trim());
          }
          using (HttpResponseMessage response = _HttpClient.Send(request)) {
            string responseBody = ReadBody(response);
            stopwatch.Stop();
            return EvaluateResponse(url, response, responseBody, stopwatch.ElapsedMilliseconds);
          }
        }
      } catch (HttpRequestException ex) {
        return InvocationResult.Create(InvocationResult._KindError, $"POST {url}\r\n\r\nTransport failure: {ex.Message}");
      } catch (TaskCanceledException) {
        return InvocationResult.Create(InvocationResult._KindError, $"POST {url}\r\n\r\nTimeout after {_HttpClient.Timeout.TotalSeconds} seconds.");
      } catch (UriFormatException ex) {
        return InvocationResult.Create(InvocationResult._KindError, $"Invalid URL '{url}': {ex.Message}");
      } catch (InvalidOperationException ex) {
        return InvocationResult.Create(InvocationResult._KindError, $"Invalid URL '{url}': {ex.Message}");
      }
    }

    /// <summary>
    /// Joins base URL and operation name with exactly one '/'.
    /// </summary>
    public static string CombineUrl(string baseUrl, string methodName) {
      return baseUrl.Trim().TrimEnd('/') + "/" + methodName;
    }

    /// <summary>
    /// Replaces the {AUTO} token and validates the resulting JSON.
    /// </summary>
    private static bool TryPrepareRequestBody(string requestText, string sideChannelText, out string requestBody, out string error) {
      requestBody = null;
      error = null;

      string sideChannel = sideChannelText;
      if (string.IsNullOrWhiteSpace(sideChannel)) {
        sideChannel = "{}";
      }
      try {
        JToken.Parse(sideChannel);
      } catch (JsonReaderException ex) {
        error = "The SideChannel content is no valid JSON: " + ex.Message;
        return false;
      }

      string body = requestText.Replace(RequestTemplateBuilder._AutoSideChannelToken, sideChannel.Trim());
      JToken parsedBody;
      try {
        parsedBody = JToken.Parse(body);
      } catch (JsonReaderException ex) {
        error = "The request is no valid JSON: " + ex.Message;
        return false;
      }
      if (parsedBody.Type != JTokenType.Object) {
        error = "The request must be a JSON object.";
        return false;
      }
      requestBody = parsedBody.ToString(Formatting.None);
      return true;
    }

    /// <summary>
    /// Reads the response body as string.
    /// </summary>
    private static string ReadBody(HttpResponseMessage response) {
      using (Stream stream = response.Content.ReadAsStream()) {
        using (StreamReader reader = new StreamReader(stream)) {
          return reader.ReadToEnd();
        }
      }
    }

    /// <summary>
    /// Classifies the response: non-2xx and invalid JSON are errors, a non-empty 'fault' is a fault.
    /// </summary>
    private static InvocationResult EvaluateResponse(string url, HttpResponseMessage response, string responseBody, long elapsedMilliseconds) {
      string header = $"POST {url}\r\nHTTP {(int)response.StatusCode} {response.ReasonPhrase} ({elapsedMilliseconds} ms)\r\n\r\n";

      if (!response.IsSuccessStatusCode) {
        return InvocationResult.Create(InvocationResult._KindError, header + responseBody);
      }

      JToken parsedResponse;
      try {
        parsedResponse = JToken.Parse(responseBody);
      } catch (JsonReaderException ex) {
        return InvocationResult.Create(InvocationResult._KindError, header + "The response is no valid UJMW JSON (" + ex.Message + "):\r\n\r\n" + responseBody);
      }

      string formattedResponse = parsedResponse.ToString(Formatting.Indented);
      JObject responseObject = parsedResponse as JObject;
      if (responseObject != null && HasFault(responseObject)) {
        return InvocationResult.Create(InvocationResult._KindFault, header + formattedResponse);
      }
      return InvocationResult.Create(InvocationResult._KindSuccess, header + formattedResponse);
    }

    /// <summary>
    /// Returns true if the 'fault' property is neither missing, null nor an empty string.
    /// </summary>
    private static bool HasFault(JObject responseObject) {
      JToken fault = responseObject.GetValue("fault", StringComparison.OrdinalIgnoreCase);
      if (fault == null || fault.Type == JTokenType.Null || fault.Type == JTokenType.Undefined) {
        return false;
      }
      if (fault.Type == JTokenType.String && string.IsNullOrWhiteSpace(fault.Value<string>())) {
        return false;
      }
      return true;
    }

  }

}
