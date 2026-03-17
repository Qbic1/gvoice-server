using System.Xml.Linq;
using GVoice.API.Models;
using System.Globalization;

namespace GVoice.API.Services;

public class XmlChatHistoryService
{
    private readonly string _historyPath;
    private static readonly SemaphoreSlim _fileLock = new(1, 1);
    private const int MaxHistoryCount = 100;

    public XmlChatHistoryService(IConfiguration configuration)
    {
        // Using a configurable path is better than hardcoding.
        _historyPath = configuration["ChatHistoryPath"] ?? "D:\chat-history";
        Directory.CreateDirectory(_historyPath);
    }

    private string GetFilePath(string roomId)
    {
        // Sanitize roomId to create a valid filename
        var sanitizedRoomId = string.Join("_", roomId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_historyPath, $"{sanitizedRoomId}.xml");
    }

    public async Task WriteMessageAsync(string roomId, ChatMessage message)
    {
        var filePath = GetFilePath(roomId);
        await _fileLock.WaitAsync();
        try
        {
            XDocument doc;
            if (File.Exists(filePath))
            {
                doc = XDocument.Load(filePath);
            }
            else
            {
                doc = new XDocument(new XElement("ChatHistory"));
            }

            var root = doc.Root!;
            root.Add(new XElement("Message",
                new XElement("DisplayName", message.DisplayName),
                new XElement("Text", message.Message),
                new XElement("Timestamp", message.Timestamp.ToString("o", CultureInfo.InvariantCulture))
            ));

            // Enforce the 100-message limit
            while (root.Elements("Message").Count() > MaxHistoryCount)
            {
                root.Elements("Message").First().Remove();
            }

            await File.WriteAllTextAsync(filePath, doc.ToString());
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<ChatMessage>> ReadHistoryAsync(string roomId)
    {
        var filePath = GetFilePath(roomId);
        var history = new List<ChatMessage>();

        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(filePath))
            {
                return history;
            }

            var doc = XDocument.Load(filePath);
            foreach (var element in doc.Root!.Elements("Message"))
            {
                history.Add(new ChatMessage
                {
                    DisplayName = element.Element("DisplayName")?.Value ?? string.Empty,
                    Message = element.Element("Text")?.Value ?? string.Empty,
                    Timestamp = DateTime.Parse(element.Element("Timestamp")?.Value ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                });
            }
        }
        catch 
        {
            // In case of XML corruption, return an empty list.
            // A logging mechanism here would be ideal in a real app.
        }
        finally
        {
            _fileLock.Release();
        }
        return history;
    }
}
