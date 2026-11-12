using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace System.Web.UJMW {

  /// <summary>
  /// JSON wire mapping of the '&lt;route&gt;/_queue' endpoint (see 'doc/epics/epic-async-queue-mode.md', 5).
  /// Used by the host-side queue controller as well as by the remote 'UjmwQueueClient', so both sides stay symmetric.
  /// </summary>
  public static class UjmwQueueWire {

    #region " Info "

    /// <summary>
    /// Serializes the info response (5.1).
    /// </summary>
    public static string SerializeInfo(UjmwQueueInfo info) {

      JObject json = new JObject();
      json["statsTotal"] = StatsToJson(info.StatsTotal);

      JObject perMethod = new JObject();
      foreach (KeyValuePair<string, UjmwQueueStats> entry in info.StatsPerMethod) {
        perMethod[entry.Key] = StatsToJson(entry.Value);
      }
      json["statsPerMethod"] = perMethod;

      JObject processors = new JObject();
      foreach (KeyValuePair<string, UjmwQueueProcessorInfo> entry in info.Processors) {

        JObject processor = new JObject();
        processor["active"] = entry.Value.Active;
        processor["capacity"] = entry.Value.Capacity;
        processor["lastActivity"] = entry.Value.LastActivity.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        processors[entry.Key] = processor;
      }
      json["processors"] = processors;

      json["fault"] = ToFaultToken(info.Fault);

      //indented for a readable browser view
      return json.ToString(Formatting.Indented);
    }

    /// <summary>
    /// Parses the info response (5.1).
    /// </summary>
    public static UjmwQueueInfo ParseInfo(string content) {

      JObject json = JObject.Parse(content);
      UjmwQueueInfo info = new UjmwQueueInfo();

      info.StatsTotal = StatsFromJson(json["statsTotal"] as JObject);

      JObject perMethod = json["statsPerMethod"] as JObject;
      if (perMethod != null) {
        foreach (JProperty property in perMethod.Properties()) {
          info.StatsPerMethod[property.Name] = StatsFromJson(property.Value as JObject);
        }
      }

      JObject processors = json["processors"] as JObject;
      if (processors != null) {
        foreach (JProperty property in processors.Properties()) {

          JObject processorJson = property.Value as JObject;
          UjmwQueueProcessorInfo processor = new UjmwQueueProcessorInfo();

          if (processorJson != null) {

            processor.Active = GetInt(processorJson, "active");

            JToken capacity = processorJson["capacity"];
            if (capacity != null && capacity.Type == JTokenType.Integer) {
              processor.Capacity = capacity.Value<int>();
            }

            JToken lastActivity = processorJson["lastActivity"];
            if (lastActivity != null && lastActivity.Type == JTokenType.Date) {
              processor.LastActivity = lastActivity.Value<DateTime>().ToUniversalTime();
            }
            else if (lastActivity != null && lastActivity.Type == JTokenType.String) {
              DateTime parsed;
              if (DateTime.TryParse(lastActivity.Value<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)) {
                processor.LastActivity = parsed;
              }
            }

          }

          info.Processors[property.Name] = processor;
        }
      }

      info.Fault = GetString(json, "fault");

      return info;
    }

    #endregion

    #region " Lease "

    /// <summary>
    /// Serializes a dequeue response (5.2). The request JSON is embedded as object ('request').
    /// </summary>
    public static string SerializeLease(UjmwQueueLease lease) {

      JObject json = new JObject();
      json["totalInQueue"] = lease.TotalInQueue;
      json["method"] = lease.MethodName;
      json["requestId"] = lease.RequestId;

      if (string.IsNullOrWhiteSpace(lease.RequestJson)) {
        json["request"] = JValue.CreateNull();
      }
      else {
        json["request"] = JToken.Parse(lease.RequestJson);
      }

      json["pollingSecret"] = lease.PollingSecret;
      json["heartbeatSec"] = lease.HeartbeatSec;
      json["fault"] = ToFaultToken(lease.Fault);

      return json.ToString(Formatting.None);
    }

    /// <summary>
    /// Parses a dequeue (or completion with 'dequeueNext') response (5.2 / 5.5).
    /// </summary>
    public static UjmwQueueLease ParseLease(string content) {

      JObject json = JObject.Parse(content);
      UjmwQueueLease lease = new UjmwQueueLease();

      lease.TotalInQueue = GetInt(json, "totalInQueue");
      lease.MethodName = GetString(json, "method");
      lease.RequestId = GetString(json, "requestId");

      JToken request = json["request"];
      if (request != null && request.Type != JTokenType.Null) {
        lease.RequestJson = request.ToString(Formatting.None);
      }

      lease.PollingSecret = GetString(json, "pollingSecret");
      lease.HeartbeatSec = GetInt(json, "heartbeatSec");
      lease.Fault = GetString(json, "fault");

      return lease;
    }

    #endregion

    #region " Heartbeat / Result "

    /// <summary>
    /// Serializes a heartbeat response (5.3).
    /// </summary>
    public static string SerializeHeartbeatResult(UjmwQueueHeartbeatResult result) {

      JObject json = new JObject();
      json["cancellationRequested"] = result.CancellationRequested;
      json["fault"] = ToFaultToken(result.Fault);

      return json.ToString(Formatting.None);
    }

    /// <summary>
    /// Parses a heartbeat response (5.3).
    /// </summary>
    public static UjmwQueueHeartbeatResult ParseHeartbeatResult(string content) {

      JObject json = JObject.Parse(content);
      UjmwQueueHeartbeatResult result = new UjmwQueueHeartbeatResult();

      JToken cancellationRequested = json["cancellationRequested"];
      if (cancellationRequested != null && cancellationRequested.Type == JTokenType.Boolean) {
        result.CancellationRequested = cancellationRequested.Value<bool>();
      }

      result.Fault = GetString(json, "fault");

      return result;
    }

    /// <summary>
    /// Serializes a plain result ('{ "fault": ... }').
    /// </summary>
    public static string SerializeResult(UjmwQueueResult result) {

      JObject json = new JObject();
      json["fault"] = ToFaultToken(result.Fault);

      return json.ToString(Formatting.None);
    }

    /// <summary>
    /// Parses a plain result ('{ "fault": ... }').
    /// </summary>
    public static UjmwQueueResult ParseResult(string content) {

      JObject json = JObject.Parse(content);
      UjmwQueueResult result = new UjmwQueueResult();
      result.Fault = GetString(json, "fault");

      return result;
    }

    #endregion

    #region " Completion "

    /// <summary>
    /// Builds the completion request body ('{ "response": {...}, "dequeueNext": "*" }', 5.5).
    /// </summary>
    public static string BuildCompletionBody(string responseJson, UjmwQueueMethodFilter dequeueNext) {

      JObject json = new JObject();

      if (string.IsNullOrWhiteSpace(responseJson)) {
        json["response"] = new JObject();
      }
      else {
        json["response"] = JToken.Parse(responseJson);
      }

      if (dequeueNext == null) {
        json["dequeueNext"] = JValue.CreateNull();
      }
      else {
        json["dequeueNext"] = dequeueNext.ToString();
      }

      return json.ToString(Formatting.None);
    }

    /// <summary>
    /// Parses the completion request body (5.5). Returns false (with error) for invalid bodies or filters.
    /// </summary>
    public static bool TryParseCompletionBody(string content, out string responseJson, out UjmwQueueMethodFilter dequeueNext, out string error) {

      responseJson = null;
      dequeueNext = null;
      error = null;

      if (string.IsNullOrWhiteSpace(content)) {
        error = "Missing completion body";
        return false;
      }

      JObject json;
      try {
        json = JObject.Parse(content);
      }
      catch (JsonException ex) {
        error = "Invalid completion body: " + ex.Message;
        return false;
      }

      JToken response = json["response"];
      if (response == null || response.Type != JTokenType.Object) {
        error = "The completion body requires a 'response' object";
        return false;
      }

      responseJson = response.ToString(Formatting.None);

      string filterExpression = GetString(json, "dequeueNext");
      if (string.IsNullOrWhiteSpace(filterExpression)) {
        return true;
      }

      return UjmwQueueMethodFilter.TryParse(filterExpression, out dequeueNext, out error);
    }

    /// <summary>
    /// Serializes the completion response: dequeue shape if 'dequeueNext' was requested (and accepted), otherwise only 'fault'.
    /// </summary>
    public static string SerializeCompletionResult(UjmwQueueLease lease, bool dequeueNextRequested) {

      if (dequeueNextRequested && lease.Fault == null) {
        return SerializeLease(lease);
      }

      JObject json = new JObject();
      json["fault"] = ToFaultToken(lease.Fault);

      return json.ToString(Formatting.None);
    }

    #endregion

    #region " Helpers "

    private static JObject StatsToJson(UjmwQueueStats stats) {

      if (stats == null) {
        stats = new UjmwQueueStats();
      }

      JObject json = new JObject();
      json["queued"] = stats.Queued;
      json["active"] = stats.Active;
      json["past1m"] = stats.Past1m;
      json["past10m"] = stats.Past10m;
      json["past1h"] = stats.Past1h;
      json["past10h"] = stats.Past10h;

      return json;
    }

    private static UjmwQueueStats StatsFromJson(JObject json) {

      UjmwQueueStats stats = new UjmwQueueStats();

      if (json == null) {
        return stats;
      }

      stats.Queued = GetInt(json, "queued");
      stats.Active = GetInt(json, "active");
      stats.Past1m = GetInt(json, "past1m");
      stats.Past10m = GetInt(json, "past10m");
      stats.Past1h = GetInt(json, "past1h");
      stats.Past10h = GetInt(json, "past10h");

      return stats;
    }

    private static JToken ToFaultToken(string fault) {

      if (fault == null) {
        return JValue.CreateNull();
      }

      return new JValue(fault);
    }

    private static string GetString(JObject json, string name) {

      JToken token = json[name];
      if (token == null || token.Type == JTokenType.Null) {
        return null;
      }

      return token.ToString();
    }

    private static int GetInt(JObject json, string name) {

      JToken token = json[name];
      if (token == null || token.Type != JTokenType.Integer) {
        return 0;
      }

      return token.Value<int>();
    }

    #endregion

  }

}
