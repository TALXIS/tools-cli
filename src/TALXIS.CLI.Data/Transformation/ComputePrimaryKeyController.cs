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
            if (string.IsNullOrWhiteSpace(input?.Id) || string.IsNullOrWhiteSpace(input?.Table))
            {
                context.Response.StatusCode = 400;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Missing 'id' or 'table' field"));
                context.Response.Close();
                return true;
            }
            var guid = ComputePrimaryKey(input.Table, input.Id);
            var response = new { primaryKey = guid };
            var json = JsonSerializer.Serialize(response);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
            context.Response.Close();
            return true;
        }
        return false;
    }


    private static Guid ComputePrimaryKey(string table, string id)
    {
        using var md5 = MD5.Create();
        var input = $"{table}:{id}";
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private class ComputePrimaryKeyRequest
    {
        public string? Id { get; set; }
        public string? Table { get; set; }
    }
}
