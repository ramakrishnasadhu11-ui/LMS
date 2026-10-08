using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading.Tasks;
using LMSWebUI.Models;
using LMSWebUI.Models.Admin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace LMSWebUI.Services
{
    public class StoreConfigurationService : IStoreConfigurationService
    {
        private readonly ILoginApiClient _loginApi;

        public StoreConfigurationService(ILoginApiClient loginApi)
        {
            _loginApi = loginApi;
        }

        public async Task<StoreConfigurationViewModel> LoadAsync(string tenantName, string storeCode)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantEmail", tenantName },
                { "storeCode", storeCode }
            };

            var (rawResponse, loadError) = await TryGetRawAsync(parameters);
            StoreConfigurationViewModel rawModel = null;
            string extractError = null;

            try
            {
                rawModel = Extract(rawResponse);
            }
            catch (Exception ex)
            {
                extractError = BuildErrorMessage(ex, "Unable to load the saved store configuration.");
            }

            if (rawModel != null)
            {
                rawModel = EnsureDefaults(Normalize(rawModel), tenantName, storeCode);
                rawModel.TenantName = tenantName;
                rawModel.StoreCode = storeCode;
                rawModel.IsExistingRecord = HasData(rawModel);
                return rawModel;
            }

            var defaultModel = BuildDefault(tenantName, storeCode);
            defaultModel.LoadErrorMessage = loadError ?? extractError;
            return defaultModel;
        }

        public async Task<(bool Success, string Message)> SaveAsync(StoreConfigurationViewModel model)
        {
            var request = new
            {
                TenantEmail = model?.TenantName,
                StoreCode = model?.StoreCode,
                ConfigurationJson = JsonConvert.SerializeObject(model),
                UpdatedBy = model?.TenantName
            };

            async Task<CustomerIOResponse> saveByLoginApiAsync()
                => await _loginApi.SendRequestAsync<CustomerIOResponse>("/NewStoreConfiguration", request, RestSharp.Method.POST);

            CustomerIOResponse response = null;

            try
            {
                response = await saveByLoginApiAsync();
            }
            catch (Exception ex)
            {
                return (false, BuildErrorMessage(ex, "Unable to save store configuration."));
            }

            var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);
            if (!isSuccess)
            {
                return (false, string.IsNullOrWhiteSpace(response?.Message)
                    ? "Unable to save store configuration."
                    : response.Message);
            }

            return (true, response?.Message);
        }

        public StoreConfigurationViewModel Normalize(StoreConfigurationViewModel model)
        {
            if (model == null)
            {
                return new StoreConfigurationViewModel();
            }

            model.StoreName = model.StoreName?.Trim();
            model.ContactPerson = model.ContactPerson?.Trim();
            model.PhoneNumber = model.PhoneNumber?.Trim();
            model.Email = model.Email?.Trim();
            model.AddressLine1 = model.AddressLine1?.Trim();
            model.AddressLine2 = model.AddressLine2?.Trim();
            model.City = model.City?.Trim();
            model.State = model.State?.Trim();
            model.PostalCode = model.PostalCode?.Trim();
            model.OpeningTime = model.OpeningTime?.Trim();
            model.ClosingTime = model.ClosingTime?.Trim();
            model.WeeklyOffDay = model.WeeklyOffDay?.Trim();
            model.Notes = model.Notes?.Trim();

            return model;
        }

        public bool HasData(StoreConfigurationViewModel model)
        {
            if (model == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(model.ContactPerson)
                   || !string.IsNullOrWhiteSpace(model.PhoneNumber)
                   || !string.IsNullOrWhiteSpace(model.Email)
                   || !string.IsNullOrWhiteSpace(model.AddressLine1)
                   || !string.IsNullOrWhiteSpace(model.AddressLine2)
                   || !string.IsNullOrWhiteSpace(model.City)
                   || !string.IsNullOrWhiteSpace(model.State)
                   || !string.IsNullOrWhiteSpace(model.PostalCode)
                   || !string.Equals(model.StoreName, model.StoreCode, StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(model.OpeningTime, "09:00", StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(model.ClosingTime, "21:00", StringComparison.OrdinalIgnoreCase)
                   || !string.IsNullOrWhiteSpace(model.Notes);
        }

        private static StoreConfigurationViewModel BuildDefault(string tenantName, string storeCode)
        {
            return new StoreConfigurationViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                IsExistingRecord = false,
                StoreName = storeCode,
                WeeklyOffDay = "Sunday",
                OpeningTime = "09:00",
                ClosingTime = "21:00"
            };
        }

        private static StoreConfigurationViewModel EnsureDefaults(StoreConfigurationViewModel model, string tenantName, string storeCode)
        {
            if (model == null)
            {
                return BuildDefault(tenantName, storeCode);
            }

            model.TenantName = string.IsNullOrWhiteSpace(model.TenantName) ? tenantName : model.TenantName;
            model.StoreCode = string.IsNullOrWhiteSpace(model.StoreCode) ? storeCode : model.StoreCode;

            model.StoreName = GetDefaultIfMissing(model.StoreName, storeCode);
            model.WeeklyOffDay = GetDefaultIfMissing(model.WeeklyOffDay, "Sunday");
            model.OpeningTime = NormalizeTimeOrDefault(model.OpeningTime, "09:00");
            model.ClosingTime = NormalizeTimeOrDefault(model.ClosingTime, "21:00");

            return model;
        }

        private static string GetDefaultIfMissing(string value, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            var normalized = value.Trim();
            if (normalized.Equals("null", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("undefined", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("--:--", StringComparison.OrdinalIgnoreCase))
            {
                return defaultValue;
            }

            return normalized;
        }

        private static string NormalizeTimeOrDefault(string value, string defaultValue)
        {
            var candidate = GetDefaultIfMissing(value, defaultValue);

            if (!TimeSpan.TryParse(candidate, CultureInfo.InvariantCulture, out var parsedTime))
            {
                return defaultValue;
            }

            return parsedTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        private async Task<(JObject Raw, string Error)> TryGetRawAsync(Dictionary<string, string> parameters)
        {
            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", parameters, RestSharp.Method.GET);
                Debug.WriteLine($"[StoreConfig][TryGetRawAsync] Response null: {response == null}");
                if (response != null)
                {
                    Debug.WriteLine($"[StoreConfig][TryGetRawAsync] Response JSON: {response}");
                }
                return (response, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StoreConfig][TryGetRawAsync][Exception] Type: {ex.GetType().FullName}");
                Debug.WriteLine($"[StoreConfig][TryGetRawAsync][Exception] Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[StoreConfig][TryGetRawAsync][Exception] Inner: {ex.InnerException.Message}");
                }
                // The API answers 404 when no configuration has been saved yet, which is a
                // valid state and must not be reported as an error.
                if (IsNotFound(ex))
                {
                    return (null, null);
                }

                return (null, BuildErrorMessage(ex, "Unable to load the saved store configuration."));
            }
        }

        private static bool IsNotFound(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                var rawMessage = current.Message;
                if (string.IsNullOrWhiteSpace(rawMessage))
                {
                    continue;
                }

                if (rawMessage.IndexOf("status code NotFound", StringComparison.OrdinalIgnoreCase) >= 0
                    || rawMessage.IndexOf("Store configuration not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                try
                {
                    if (rawMessage.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    {
                        var parsed = JToken.Parse(rawMessage);
                        var statusCode = parsed["statusCode"]?.ToString()
                                         ?? parsed["StatusCode"]?.ToString();

                        if (string.Equals(statusCode, "404", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static StoreConfigurationViewModel Extract(JObject rawResponse)
        {
            Debug.WriteLine($"[StoreConfig][Extract] Raw response: {(rawResponse == null ? "<null>" : rawResponse.ToString())}");

            if (rawResponse == null)
            {
                return null;
            }

            var dataToken = rawResponse["ResultSet"]
                ?? rawResponse["resultSet"]
                ?? rawResponse["StoreConfiguration"]
                ?? rawResponse["storeConfiguration"]
                ?? rawResponse["Data"]
                ?? rawResponse["data"]
                ?? rawResponse;

            if (dataToken is JArray arr && arr.Count > 0)
            {
                dataToken = arr[0];
            }

            if (dataToken?["$values"] is JArray wrapped && wrapped.Count > 0)
            {
                dataToken = wrapped[0];
            }

            if (dataToken is not JObject obj)
            {
                return null;
            }

            var configurationJson = obj["ConfigurationJson"]?.ToString() ?? obj["configurationJson"]?.ToString();
            if (!string.IsNullOrWhiteSpace(configurationJson))
            {
                Debug.WriteLine($"[StoreConfig][Extract] configurationJson before parse: {configurationJson}");
                try
                {
                    var normalizedConfigurationJson = configurationJson.Trim();
                    if (normalizedConfigurationJson.Length > 1
                        && normalizedConfigurationJson[0] == '"'
                        && normalizedConfigurationJson[^1] == '"')
                    {
                        normalizedConfigurationJson = JsonConvert.DeserializeObject<string>(normalizedConfigurationJson);
                    }

                    if (string.IsNullOrWhiteSpace(normalizedConfigurationJson))
                    {
                        throw new JsonException("ConfigurationJson is empty after unwrapping.");
                    }

                    var parsedModel = JsonConvert.DeserializeObject<StoreConfigurationViewModel>(normalizedConfigurationJson);
                    Debug.WriteLine($"[StoreConfig][Extract] Parsed model => StoreName: {parsedModel?.StoreName}, ContactPerson: {parsedModel?.ContactPerson}, PhoneNumber: {parsedModel?.PhoneNumber}, Email: {parsedModel?.Email}");
                    if (parsedModel != null)
                    {
                        return parsedModel;
                    }

                    throw new JsonException("ConfigurationJson deserialized to null StoreConfigurationViewModel.");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Unable to deserialize ConfigurationJson from store configuration response.", ex);
                }
            }

            return new StoreConfigurationViewModel
            {
                StoreName = obj["StoreName"]?.ToString() ?? obj["storeName"]?.ToString(),
                ContactPerson = obj["ContactPerson"]?.ToString() ?? obj["contactPerson"]?.ToString(),
                PhoneNumber = obj["PhoneNumber"]?.ToString() ?? obj["phoneNumber"]?.ToString() ?? obj["ContactNo"]?.ToString() ?? obj["MobileNo"]?.ToString(),
                Email = obj["Email"]?.ToString() ?? obj["email"]?.ToString(),
                AddressLine1 = obj["AddressLine1"]?.ToString() ?? obj["addressLine1"]?.ToString() ?? obj["Address1"]?.ToString(),
                AddressLine2 = obj["AddressLine2"]?.ToString() ?? obj["addressLine2"]?.ToString() ?? obj["Address2"]?.ToString(),
                City = obj["City"]?.ToString() ?? obj["city"]?.ToString(),
                State = obj["State"]?.ToString() ?? obj["state"]?.ToString(),
                PostalCode = obj["PostalCode"]?.ToString() ?? obj["postalCode"]?.ToString() ?? obj["Pincode"]?.ToString(),
                OpeningTime = obj["OpeningTime"]?.ToString() ?? obj["openingTime"]?.ToString() ?? obj["OpenTime"]?.ToString(),
                ClosingTime = obj["ClosingTime"]?.ToString() ?? obj["closingTime"]?.ToString() ?? obj["CloseTime"]?.ToString(),
                WeeklyOffDay = obj["WeeklyOffDay"]?.ToString() ?? obj["weeklyOffDay"]?.ToString() ?? obj["WeeklyOff"]?.ToString(),
                Notes = obj["Notes"]?.ToString() ?? obj["notes"]?.ToString() ?? obj["Description"]?.ToString()
            };
        }

        private static string BuildErrorMessage(Exception ex, string fallbackMessage)
        {
            var rawMessage = ex?.InnerException?.Message ?? ex?.Message;
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return fallbackMessage;
            }

            try
            {
                if (rawMessage.StartsWith("{", StringComparison.Ordinal))
                {
                    var token = JToken.Parse(rawMessage);
                    var message = token["Message"]?.ToString()
                                  ?? token["message"]?.ToString()
                                  ?? token["ResultSet"]?["Message"]?.ToString()
                                  ?? token["resultSet"]?["message"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message;
                    }
                }
            }
            catch
            {
            }

            return $"{fallbackMessage} {rawMessage}";
        }
    }
}
