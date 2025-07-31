using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace TALXIS.CLI.Data.DataServer;

public class ComputePrimaryKeyController
{
    public static async Task<bool> TryHandle(HttpListenerContext context)
    {
        if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/ComputePrimaryKey")
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            var input = JsonSerializer.Deserialize<ComputePrimaryKeyRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (string.IsNullOrWhiteSpace(input?.Entity) || input?.AlternateKeys == null || input.AlternateKeys.Count == 0)
            {
                context.Response.StatusCode = 400;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Missing 'entity' or 'alternateKeys' field"));
                context.Response.Close();
                return true;
            }
            var guid = ComputePrimaryKey(input.Entity, input.AlternateKeys);
            var response = new { primaryKey = guid };
            var json = JsonSerializer.Serialize(response);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
            context.Response.Close();
            return true;
        }
        return false;
    }


    private static Guid ComputePrimaryKey(string entity, Dictionary<string, object> alternateKeys)
    {
        using var md5 = MD5.Create();
        // Sort keys for deterministic order
        var sortedKeys = alternateKeys.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);
        var concat = new StringBuilder();
        concat.Append(entity).Append(":");
        foreach (var kvp in sortedKeys)
        {
            concat.Append(kvp.Key).Append("=");
            concat.Append(kvp.Value?.ToString() ?? "null").Append(";");
        }
        var input = concat.ToString();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private class ComputePrimaryKeyRequest
    {
        public string? Entity { get; set; }
        public Dictionary<string, object>? AlternateKeys { get; set; }
    }
}
