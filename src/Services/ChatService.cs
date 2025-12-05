using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZavaStorefront.Models;

namespace ZavaStorefront.Services
{
    public class ChatService
    {
        private readonly HttpClient _httpClient;
        private readonly ChatSettings _settings;
        private readonly ILogger<ChatService> _logger;

        public ChatService(HttpClient httpClient, IOptions<ChatSettings> settings, ILogger<ChatService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<ChatResponse> SendMessageAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return new ChatResponse
                {
                    Success = false,
                    Error = "Message cannot be empty."
                };
            }

            if (string.IsNullOrWhiteSpace(_settings.EndpointUrl))
            {
                _logger.LogWarning("Chat endpoint URL is not configured");
                return new ChatResponse
                {
                    Success = false,
                    Error = "Chat service is not configured. Please set the endpoint URL in appsettings.json."
                };
            }

            try
            {
                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 800,
                    temperature = 0.7
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Set API key header if configured
                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                }

                _logger.LogInformation("Sending message to Phi4 endpoint");
                var response = await _httpClient.PostAsync(_settings.EndpointUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("API call failed with status {StatusCode}: {Error}", 
                        response.StatusCode, errorContent);
                    return new ChatResponse
                    {
                        Success = false,
                        Error = $"API call failed: {response.StatusCode}"
                    };
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Received response from Phi4 endpoint");

                // Parse the response - Microsoft Foundry uses OpenAI-compatible format
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;

                if (root.TryGetProperty("choices", out var choices) && 
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var messageContent))
                    {
                        return new ChatResponse
                        {
                            Success = true,
                            Message = messageContent.GetString() ?? string.Empty
                        };
                    }
                }

                // Fallback for different response formats
                if (root.TryGetProperty("output", out var output))
                {
                    return new ChatResponse
                    {
                        Success = true,
                        Message = output.GetString() ?? string.Empty
                    };
                }

                return new ChatResponse
                {
                    Success = false,
                    Error = "Unable to parse response from AI endpoint."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while calling Phi4 endpoint");
                return new ChatResponse
                {
                    Success = false,
                    Error = "Network error: Unable to connect to the AI service."
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request to Phi4 endpoint timed out");
                return new ChatResponse
                {
                    Success = false,
                    Error = "Request timed out. Please try again."
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error parsing response from Phi4 endpoint");
                return new ChatResponse
                {
                    Success = false,
                    Error = "Error parsing response from AI service."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calling Phi4 endpoint");
                return new ChatResponse
                {
                    Success = false,
                    Error = "An unexpected error occurred. Please try again."
                };
            }
        }
    }
}
