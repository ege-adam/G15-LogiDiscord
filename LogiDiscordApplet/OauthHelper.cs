using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;

namespace LogiDiscordApplet
{
    public static class OauthHelper
    {
        public static async Task<string> GetAccessTokenAsync(string code, string clientId, string clientSecret)
        {
            string url = "https://discord.com/api/oauth2/token";

            //request token
            var restclient = new RestClient(url);
            RestRequest request = new RestRequest() { Method = Method.Post };
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");

            request.AddParameter("client_id", clientId);
            request.AddParameter("client_secret", clientSecret);
            request.AddParameter("code", code);
            request.AddParameter("grant_type", "authorization_code");

            var tResponse = await restclient.ExecuteAsync(request);
            var responseJson = tResponse.Content;
            Console.WriteLine(responseJson);
            var token = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson)["access_token"].ToString();
            return token.Length > 0 ? token : null;
        }
    }
}
