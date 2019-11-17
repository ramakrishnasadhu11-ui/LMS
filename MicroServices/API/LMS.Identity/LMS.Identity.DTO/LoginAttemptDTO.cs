using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class LoginAttemptDto
    {
        [DataMember]
        [JsonProperty("loginAttemptId")]
        public int LoginAttemptId { get; set; }

        [DataMember]
        [JsonProperty("userName")]
        public string UserName { get; set; }

        [DataMember]
        [JsonProperty("password")]
        public string Password { get; set; }

        [DataMember]
        [JsonProperty("ipnumber")]
        public string Ipnumber { get; set; }

        [DataMember]
        [JsonProperty("browserType")]
        public string BrowserType { get; set; }

        [DataMember]
        [JsonProperty("success")]
        public bool? Success { get; set; }

        [DataMember]
        [JsonProperty("createdDate")]
        public DateTime? CreatedDate { get; set; }

        public static LoginAttemptDto FromModel(LoginAttempt model)
        {
            return new LoginAttemptDto()
            {
                LoginAttemptId = model.LoginAttemptId, 
                UserName = model.UserName, 
                Password = model.Password, 
                Ipnumber = model.Ipnumber, 
                BrowserType = model.BrowserType, 
                Success = model.Success, 
                CreatedDate = model.CreatedDate, 
            }; 
        }

        public LoginAttempt ToModel()
        {
            return new LoginAttempt()
            {
                LoginAttemptId = LoginAttemptId, 
                UserName = UserName, 
                Password = Password, 
                Ipnumber = Ipnumber, 
                BrowserType = BrowserType, 
                Success = Success, 
                CreatedDate = CreatedDate, 
            }; 
        }
    }
}