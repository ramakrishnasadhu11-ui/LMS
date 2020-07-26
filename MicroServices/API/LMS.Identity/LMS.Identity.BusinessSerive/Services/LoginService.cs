using LMS.Identity.BusinessSerive.Interfaces;
using MongoDB.Driver;
using LMS.Identity.DTO.Entities;
using Microsoft.AspNetCore.Http;
using System.Security.Authentication;
using AutoMapper;
using LMS.Identity.DTO;
using System;
using System.Threading.Tasks;
using LMS.Identity.Utilities;
using System.Net;
using LMS.Identity.BusinessSerive.Common;
using System.Collections.Generic;
using System.Text;
using System.Net.Mail;
using System.IO;
using SendGrid.Helpers.Mail;
using SendGrid;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
        private IMapper _mapper { get; }
        private readonly MongoClientSettings _tenantMongoClientSettings = null;
        private readonly IMongoDatabase _tenantDatabase = null;
        private readonly IMongoCollection<TenantEntity> _tenantRegisry;
        private readonly IMongoCollection<TenantStoreInfoEntity> _tenantStores;
         private readonly ITenantRegistryConnection _tenantRegistryConnection;
         private readonly IMailConfiguration _mailConfiguration;
        private readonly string _mailSubject,_mailFrom,_sender,_smtpServer,_reciever,_username,_password;
         private readonly int _port;

        public LoginService(IHttpContextAccessor httpContextAccessor, ITenantRegistryConnection tenantRegistryConnection, IMailConfiguration mailConfiguration,IMapper mapper)
        {
             _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _tenantRegistryConnection = tenantRegistryConnection;
            _mailConfiguration = mailConfiguration;
            _tenantMongoClientSettings = MongoClientSettings.FromUrl(new MongoUrl(_tenantRegistryConnection.ConnectionString));
            _mailFrom=_mailConfiguration.MailFrom;
            _sender=_mailConfiguration.Sender;
            _smtpServer=_mailConfiguration.SmtpServer;
            _reciever=_mailConfiguration.Reciever;
            _port=_mailConfiguration.Port;
            _username=_mailConfiguration.Username;
             _username=_mailConfiguration.Username;
            _password=_mailConfiguration.Password;
            _mailSubject=_mailConfiguration.MailSubject;
            _tenantMongoClientSettings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            _tenantMongoClientSettings.ConnectTimeout = new System.TimeSpan(_tenantRegistryConnection.ConnectTimeoutInSeconds * System.TimeSpan.TicksPerSecond);
            var tenantRegisterClient = new MongoClient(_tenantMongoClientSettings);
            if (tenantRegisterClient != null)
                _tenantDatabase = tenantRegisterClient.GetDatabase(_tenantRegistryConnection.DatabaseName);

             _tenantRegisry = _tenantDatabase.GetCollection<TenantEntity>(_tenantRegistryConnection.TenantRegisryCollectionName);
            _tenantStores = _tenantDatabase.GetCollection<TenantStoreInfoEntity>(_tenantRegistryConnection.TenantStoreInfoCollectionName);
        }

           public async Task<ActionReturnType> Register(TenantDto tenantDto)
           {
            if(tenantDto==null)
            {
                 return ActionSet.ActionReturnType(HttpStatusCode.NoContent, new IOResponse { TenantId = "", Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_ERROR_MESSAGE });
            }
            else if(tenantDto!=null)
            {
                 var emailfilter = Builders<TenantEntity>.Filter.Where(em => em.Email == tenantDto.Email);
                 var result = await _tenantRegisry.Find(emailfilter).FirstOrDefaultAsync();
                 if (result!=null)
                   {
                     return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new IOResponse { Message = IdentityValidationMessage.IDENTITY_DATAFOUND_ERROR_MESSAGE});
                   }
           
                var tenantData = _mapper.Map<TenantEntity>(tenantDto);
                var guid = Guid.NewGuid();
                var tenantid = Convert.ToString(guid);
                tenantData.TenantId=tenantid;
                tenantData.ModifiedDate=DateTime.UtcNow;
                tenantData.CreatedDate=DateTime.UtcNow;
                await  _tenantRegisry.InsertOneAsync(tenantData);
                await CreateTenantUserInfoAsync(tenantDto,tenantid);
                return ActionSet.ActionReturnType(HttpStatusCode.Created, new IOResponse { TenantId = tenantid, Message = IdentityValidationMessage.IDENTITY_INSERT_SUCCESS_MESSAGE, TenantName = tenantDto.TenantName  });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { TenantId = "", Message = IdentityValidationMessage.IDENTITY_INSERT_ERROR_MESSAGE });
           }
        public async Task<ActionReturnType> TenantLogin(string eMail,string password)
        {
            if(!string.IsNullOrEmpty(eMail))
            {
            var emailfilter = Builders<TenantEntity>.Filter.Where(em => em.Email == eMail.Trim());
            var tenantregistryresult = await _tenantRegisry.Find(emailfilter).FirstOrDefaultAsync();
                if(tenantregistryresult==null)
                  return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                    string tenantId=tenantregistryresult.TenantId;
                    var passwordfilter=Builders<TenantStoreInfoEntity>.Filter.Where(tsi => tsi.TenantId ==tenantId && tsi.Password== password);
                    var tenantstoreinforesult= await _tenantStores.Find(passwordfilter).FirstOrDefaultAsync();

                if(tenantstoreinforesult!=null && tenantstoreinforesult.IsPasswordChanged==false)
                {
                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGE_MESSAGE });
                }
                else if(tenantstoreinforesult!=null && tenantstoreinforesult.IsPasswordChanged==true)
                {
                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_LOGIN_MESSAGE });
                }
             }
           else
            { 
                 return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
            }
             return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        public async Task<ActionReturnType> forgotPassword(string eMail)
        {
            if(!string.IsNullOrEmpty(eMail))
            {
                 var emailfilter = Builders<TenantEntity>.Filter.Where(em => em.Email == eMail.Trim());
                 var tenantregistryresult = await _tenantRegisry.Find(emailfilter).FirstOrDefaultAsync();
                if(tenantregistryresult==null)
                  return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                    string tenantId=tenantregistryresult.TenantId;
                    var passwordfilter=Builders<TenantStoreInfoEntity>.Filter.Where(tsi => tsi.TenantId ==tenantId);
                    var tenantstoreinforesult= await _tenantStores.Find(passwordfilter).FirstOrDefaultAsync();
                if(tenantstoreinforesult!=null)
                {
                     string password = string.Empty;
                     password = Utility.encode(eMail,8);
          
                    var updatefilter = Builders<TenantStoreInfoEntity>.Update
                        .Set(tsi => tsi.IsPasswordChanged, false)
                        .Set(tsi => tsi.Password, password);

                      await _tenantStores.UpdateOneAsync(passwordfilter, updatefilter);
                     sendEmailToChangePassword(eMail, password);

                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE });
                }
                else
                { 
                      return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
                }
            }
            else
            {
                  return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
            }
        }

        public async Task<ActionReturnType> changePassword(string eMail,string oldPassword,string newPassword)
        {
            if(!string.IsNullOrEmpty(eMail) && !string.IsNullOrEmpty(oldPassword) &&  !string.IsNullOrEmpty(newPassword))
            {
                 var emailfilter = Builders<TenantEntity>.Filter.Where(em => em.Email == eMail.Trim());
                 var tenantregistryresult = await _tenantRegisry.Find(emailfilter).FirstOrDefaultAsync();
                if(tenantregistryresult==null)
                  return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                    string tenantId=tenantregistryresult.TenantId;
                    var passwordfilter=Builders<TenantStoreInfoEntity>.Filter.Where(tsi => tsi.TenantId ==tenantId && tsi.Password== oldPassword);
                    var tenantstoreinforesult= await _tenantStores.Find(passwordfilter).FirstOrDefaultAsync();
                 if(tenantstoreinforesult!=null && tenantstoreinforesult.IsPasswordChanged==false && tenantstoreinforesult.Password==oldPassword.Trim())
                {
                     var updatefilter = Builders<TenantStoreInfoEntity>.Update
                        .Set(tsi => tsi.IsPasswordChanged, true)
                        .Set(tsi => tsi.Password, newPassword);
                      await _tenantStores.UpdateOneAsync(passwordfilter, updatefilter);
                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE });
                }
                else if(tenantstoreinforesult!=null && tenantstoreinforesult.IsPasswordChanged==true)
                {
                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE });
                }
            }
            else
            {
              return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANTPASSWORDDATA_NOT_FOUND });
            }
              return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        private async Task<bool> CreateTenantUserInfoAsync(TenantDto tenantDto,string tenantid)
        {
            string password = string.Empty;
            password = Utility.encode(tenantDto.Email,8);
            TenantStoreInfoEntity TenantStoreInfoEntity=new TenantStoreInfoEntity();
            TenantStoreInfoEntity.TenantId=tenantid;
            TenantStoreInfoEntity.Name=tenantDto.TenantName;
            TenantStoreInfoEntity.Status="ÏnActive";
            TenantStoreInfoEntity.IsPasswordChanged=false;
            TenantStoreInfoEntity.Password=password;
            List<string> storeCodes = new List<string>();
            for (int i = 0; i < tenantDto.NoOfStores; i++)
            {
                 string code = Utility.GenerateStoreCode(3);
                storeCodes.Add(code);
            }
            TenantStoreInfoEntity.Storecodes=storeCodes;
            await _tenantStores.InsertOneAsync(TenantStoreInfoEntity);
            sendEmailToRegisterTenant(tenantDto.Email, password,storeCodes);
            return true;
        }
        private bool sendEmailToChangePassword(string eMail,string password)
        {
            StringBuilder sb = new StringBuilder();
             sb.Append("<table border=\"1\">");
             sb.Append("<tr bgcolor=\"#B2BEB5\"><td><b>Tenant Email</td><td><b>Password</td></tr>");
             sb.Append("<tr>" +
                       "<td>" + eMail + "</td>" +
                       "<td>" + password + "</td>" +
                       "</tr>");
             sb.Append("</table>");
             sb.Append("</br> </br> </br> </br> </br></br>");
             sb.Append("<p>P.S. This is an automated email please do not reply.</p>");
             sb.Append("</br>");
             sb.Append("Regards,");
             sb.Append("</br>");
             sb.Append("LMS Inc");
             string ReportSubject = _mailSubject;
             string ReportBody = sb.ToString();
            string FileName=string.Empty;
            SendMail(eMail,FileName, ReportSubject, ReportBody);
            return true;

        }
        private bool sendEmailToRegisterTenant(string eMail,string password,List<string> storeCodes)
        {
            StringBuilder sb = new StringBuilder();
             sb.Append("<table border=\"1\">");
             sb.Append("<tr bgcolor=\"#B2BEB5\"><td><b>Tenant Email</td><td><b>Password</td><td><b>Store Count</td><td><b>SoreCodes</td><td><b>Status</td></tr>");
             sb.Append("<tr>" +
                       "<td>" + eMail + "</td>" +
                       "<td>" + password + "</td>" +
                       "<td bgcolor=\"#13EC31\">" + storeCodes.Count + "</td>" +
                       "<td bgcolor=\"#13EC31\">" + string.Join(",", storeCodes)  + "</td>" +
                       "<td bgcolor=\"#13EC31\"> InActive </td>" +
                       "</tr>");
             sb.Append("</table>");
             sb.Append("</br> </br> </br> </br> </br></br>");
             sb.Append("<p>P.S. This is an automated email please do not reply.</p>");
             sb.Append("</br>");
             sb.Append("Regards,");
             sb.Append("</br>");
             sb.Append("LMS Inc");
             string ReportSubject = _mailSubject;
             string ReportBody = sb.ToString();
            string FileName=string.Empty;
            SendMail(eMail,FileName, ReportSubject, ReportBody);
            return true;
        }
         public async void SendMail(string eMail,string successFile, string subject, string body)
        {
            try
            {
                var apiKey ="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";
                var client = new SendGridClient(apiKey);
                var from = new EmailAddress(_mailFrom, "Excel Laundry Services");
                  List<EmailAddress> tos = new List<EmailAddress>
                  {
                      new EmailAddress(eMail, "ELS Tenant"),
                  };
        
               var msg = MailHelper.CreateSingleEmailToMultipleRecipients(from, tos, subject, "", body, false);
               var response = await client.SendEmailAsync(msg);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public class TenantRegistryConnection : ITenantRegistryConnection
        {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string TenantRegisryCollectionName { get; set; }
        public string TenantStoreInfoCollectionName { get; set; }
        public int ConnectTimeoutInSeconds { get; set; }
        }
         public interface ITenantRegistryConnection
        {
        string ConnectionString { get; set; }
        string DatabaseName { get; set; }
        string TenantRegisryCollectionName { get; set; }
        string TenantStoreInfoCollectionName {get;set;}
        int ConnectTimeoutInSeconds { get; set; }
            
        }

         public class MailConfiguration : IMailConfiguration
        {
        public string MailSubject { get; set; }
        public string MailFrom { get; set; }
        public string Sender { get; set; }
        public string SmtpServer {get;set;}
        public string Reciever { get; set; }
        public int Port { get; set; }
        public string Username {get;set;}
        public string Password {get;set;}
        public string AlertMailSubject {get;set;}
        public string ErrorMessage {get;set;}
        public string SuccessMessage {get;set;}
        }
       
        public interface IMailConfiguration
        {
        string MailSubject { get; set; }
        string MailFrom { get; set; }
        string Sender { get; set; }
        string SmtpServer {get;set;}
        string Reciever {get;set;}
        int Port { get; set; }
        string Username {get;set;}
        string Password {get;set;}
        string AlertMailSubject {get;set;}
        string ErrorMessage {get;set;}
        string SuccessMessage {get;set;}
        }

               //public async Task<int> ForgotPassword(string email)
        //{
        //    var ctx = new LMSDB_DevContext();
        //    string password = string.Empty;
        //    password = Utility.generateOTP();
        //    var @Status = new SqlParameter("@Status", SqlDbType.Int);
        //    @Status.Direction = ParameterDirection.Output;
        //    int st =ctx.Database.ExecuteSqlCommand("[dbo].[usp_clientForgotPassword] @Email,@NewPassword,@Status OUT",
        //    new SqlParameter("@Email", email),
        //    new SqlParameter("@NewPassword", password),
        //    Status);
        //    if (Convert.ToInt32(Status.Value)==1)
        //    {
        //        sendEmailToUser(email, password);
        //        return Convert.ToInt32(Status.Value);
        //    }
        //    else
        //        return 0;
        //}
        
         
       
        //public async Task<int> ClientChangePassword(string email, string NewPassword, string OldPassword)
        //{
        //        var ctx = new LMSDB_DevContext();
        //        var Status = new SqlParameter("@Status", SqlDbType.Int);
        //        @Status.Direction = ParameterDirection.Output;
        //        ctx.Database.ExecuteSqlCommand("[dbo].[usp_clientChangePassword] @MailId,@NewPassword,@OLdPassword,@Status OUT",
        //             new SqlParameter("@MailId", email),
        //             new SqlParameter("@NewPassword", NewPassword),
        //             new SqlParameter("@OLdPassword", OldPassword),
        //             @Status);
        //        if (Convert.ToInt32(@Status.Value) == 1)
        //            return 1;
        //        else
        //            return 0;
        //}
        //public int GetEmailCount(string email)
        //{
        //        var ctx = new LMSDB_DevContext();
        //        var Count = new SqlParameter("@Count", SqlDbType.Int);
        //        @Count.Direction = ParameterDirection.Output;
        //        ctx.Database.ExecuteSqlCommand("[dbo].[usp_CheckUserEmailCount] @MailId,@Count OUT",
        //             new SqlParameter("@MailId", email),
        //             @Count);
        //        if(Convert.ToInt32(@Count.Value) == 1)
        //            return 1;
        //        else
        //            return 0;
        //}

        

        //}

        //#region GetClientStoreDetails
        //public string GetClientStoreDetails(string eMail)
        //{
        //    var ctx = new LMSDB_DevContext();
        //    var count = new SqlParameter("@Count", SqlDbType.Int);
        //    count.Direction = ParameterDirection.Output;
        //    var StoresDetails = new SqlParameter("@StoresDetails", SqlDbType.VarChar);
        //    StoresDetails.Direction = ParameterDirection.Output;
        //    StoresDetails.Size = 100;

        //    ctx.Database.ExecuteSqlCommand("[dbo].[usp_CheckClientStoresCount] @Email,@Count OUT",
        //         new SqlParameter("@Email", eMail),
        //         count);

        //    if(Convert.ToInt32(count.Value)>=1)
        //    {
        //        ctx.Database.ExecuteSqlCommand("[dbo].[usp_GetStoresDetailsByClient] @Email,@StoresDetails OUT",
        //       new SqlParameter("@Email", eMail),
        //       StoresDetails);

        //        if(StoresDetails.Value.ToString().Length>0)
        //        {
        //            return StoresDetails.Value.ToString();
        //        }
        //        else
        //        {
        //            return string.Empty;
        //        }
        //    }
        //    return string.Empty;

            
        //}
        //#endregion

        //#region CheckIsPasswordChanged
        //public bool CheckIsPasswordChangedByclient(string eMail)
        //{
        //    var ctx = new LMSDB_DevContext();
        //    var isPasswordChanged = new SqlParameter("@isPasswordChanged", SqlDbType.Bit);
        //    isPasswordChanged.Direction = ParameterDirection.Output;

        //    ctx.Database.ExecuteSqlCommand("[dbo].[usp_checkIsPasswordChangedByclient] @Email,@isPasswordChanged OUT",
        //         new SqlParameter("@Email", eMail),
        //         isPasswordChanged);

        //    return Convert.ToBoolean(isPasswordChanged.Value);
        //}
        //#endregion
    }
}
