using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AutoMapper;
using LMS.Core.Repository.UnitOfWork;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Common;
using LMS.Identity.DataModels.DataContext;
using LMS.Identity.DTO.Entities.Dto;
using Microsoft.EntityFrameworkCore;
using AutoMapper.Configuration;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using System.Net;
using LMS.Identity.DTO;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;

        public LoginService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            this._unitOfWork = unitOfWork;
            this._mapper = mapper;
        }

        #region AddCustomer
        public int RegisterUser(CustomerDto CustomertDto)
        {
           
             var ctx = new LMSDB_DevContext();
            var customerId = new SqlParameter("@CustomerId", SqlDbType.Int);
            customerId.Direction = ParameterDirection.Output;
            CustomertDto.Active = false;
                 int status =ctx.Database.ExecuteSqlCommand("[dbo].usp_insert_customer @CustomerGenderID,@CustomerName,@phoneNumber,@EMail,@MiddleName,@FamilyName,@Photo,@Address1,@Address2,@City,@Region,@Zip,@Country,@Active,@CreatedByUserId,@ModifiedByUserId,@CustomerId OUT",
                 new SqlParameter("@CustomerGenderID", CustomertDto.CustomerGenderId),
                 new SqlParameter("@CustomerName", CustomertDto.CustomerName),
                 new SqlParameter("@phoneNumber", CustomertDto.PhoneNumber),
                 new SqlParameter("@EMail", CustomertDto.Email),
                 new SqlParameter("@MiddleName", CustomertDto.MiddleName),
                 new SqlParameter("@FamilyName", CustomertDto.FamilyName),
                 new SqlParameter("@Photo", CustomertDto.Photo),
                 new SqlParameter("@Address1", CustomertDto.Address1),
                 new SqlParameter("@Address2", CustomertDto.Address2),
                 new SqlParameter("@City", CustomertDto.City),
                 new SqlParameter("@Region", CustomertDto.Region),
                 new SqlParameter("@Zip", CustomertDto.Zip),
                 new SqlParameter("@Country", CustomertDto.Country),
                 new SqlParameter("@Active", CustomertDto.Active),
                 new SqlParameter("@CreatedByUserId", CustomertDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", CustomertDto.ModifiedByUserId),
                 customerId);

            if (status == 1)
            {
                string password = string.Empty;
                password = Utility.encode(CustomertDto.Email);
                var passwordgenStatus = new SqlParameter("@Status", SqlDbType.Int);
                passwordgenStatus.Direction = ParameterDirection.Output;
                 int st = ctx.Database.ExecuteSqlCommand("usp_client_generatepassword @User_id,@Password,@Active,@CreatedByUserId,@ModifiedByUserId,@Status OUT",
                 new SqlParameter("@User_id", Convert.ToInt32(customerId.Value)),
                 new SqlParameter("@Password", password),
                 new SqlParameter("@Active", 1),
                 new SqlParameter("@CreatedByUserId", CustomertDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", CustomertDto.ModifiedByUserId),
                 passwordgenStatus);
                if (Convert.ToInt32(customerId.Value) > 0 && Convert.ToInt32(passwordgenStatus.Value) > 0)
                {
                   sendEmailToUser(CustomertDto.Email, password);
                    return Convert.ToInt32(customerId.Value);
                }
                else
                    return 0;
            }
            else
                return 0;
        }
        #endregion
        public async Task<int> ForgotPassword(string email)
        {
            var ctx = new LMSDB_DevContext();
            string password = string.Empty;
            password = Utility.generateOTP();
            var @Status = new SqlParameter("@Status", SqlDbType.Int);
            @Status.Direction = ParameterDirection.Output;
            int st =ctx.Database.ExecuteSqlCommand("[dbo].[usp_clientForgotPassword] @Email,@NewPassword,@Status OUT",
            new SqlParameter("@Email", email),
            new SqlParameter("@NewPassword", password),
            Status);
            if (Convert.ToInt32(Status.Value)==1)
            {
                sendEmailToUser(email, password);
                return Convert.ToInt32(Status.Value);
            }
            else
                return 0;
        }
        public bool sendEmailToUser(string eMail,string password)
        {
            Console.WriteLine("Sending Email...");
            string key1 = string.Empty;
            string key2 = string.Empty;
            key1 = "AKIA6BLJZLG25YT4C6E7";
            key2 = "Gb9Zc50N2AYCNFX00IV+wCuPzzbxvq0u/lmCTJqB";
            using (var client = new AmazonSimpleEmailServiceClient(key1, key2, Amazon.RegionEndpoint.USEast1))
            {
                var sendRequest = new SendEmailRequest
                {
                    Source = "laundrymanagementsoftware@gmail.com",
                    Destination = new Destination { ToAddresses = { eMail } },
                    Message = new Message
                    {
                        Subject = new Content("Hello from the Amazon Simple Email Service!"),
                        Body = new Body
                        {
                            Html = new Content("<html>" +
                                               "<body>" +
                                               "<h2>Hello from Laundry Management Software Mail Service</h2>" +
                                               "<ul>" +
                                               "<li><b>Thanks for Registration</b></li>" +
                                               "<b>Your Password is</b>" +
                                               "<b>" +
                                               password +
                                               "</b>" +
                                               "</body>" +
                                               "</html>")
                        }
                    }
                };
                var response = client.SendEmailAsync(sendRequest).Result;
                return response.HttpStatusCode == HttpStatusCode.OK;
            }

        }
        public bool sendEmailToClient(string eMail, string password,List<string> storeCodes)
        {
            string store_codes = string.Empty;
            foreach(string stcode in storeCodes)
            {
                store_codes += stcode + ",";
            }
            Console.WriteLine("Sending Email...");
            string key1 = string.Empty;
            string key2 = string.Empty;
            key1 = "AKIA6BLJZLG25YT4C6E7";
            key2 = "Gb9Zc50N2AYCNFX00IV+wCuPzzbxvq0u/lmCTJqB";
            using (var client = new AmazonSimpleEmailServiceClient(key1, key2, Amazon.RegionEndpoint.USEast1))
            {
                var sendRequest = new SendEmailRequest
                {
                    Source = "laundrymanagementsoftware@gmail.com",
                    Destination = new Destination { ToAddresses = { eMail } },
                    Message = new Message
                    {
                        Subject = new Content("EDS Software!"),
                        Body = new Body
                        {
                            Html = new Content("<html>" +
                                               "<body>" +
                                               "<h2>Welcome to Excel Dry Cleaning Mail Service</h2>" +
                                               "<ul>" +
                                               "<li><b>Thanks for Registration</b></li>" +
                                               "<b>Password: </b>" +
                                               "<b>" +
                                               password +
                                               "</b><br>" +
                                               "<b>Store Code: </b>" +
                                               "<b>" +
                                               store_codes +
                                               "</b><br>" +
                                               "</body>" +
                                               "</html>")
                        }
                    }
                };
                var response = client.SendEmailAsync(sendRequest).Result;
                return response.HttpStatusCode == HttpStatusCode.OK;
            }

        }
        public async Task<List<UserGenderDto>> UserGender()
        {
            var ctx = new LMSDB_DevContext();
                var list = await ctx.UserGender.FromSql("exec[dbo].[usp_get_gender]").ToListAsync();
            return _mapper.Map<List<UserGenderDto>>(list);
        }

        public async Task<int> CheckUserEmailExist(string email)
        {
            var ctx = new LMSDB_DevContext();

            var AlreadyExistYesNo = new SqlParameter("@AlreadyExistYesNo", SqlDbType.Int);
            AlreadyExistYesNo.Direction = ParameterDirection.Output;

            ctx.Database.ExecuteSqlCommand("[dbo].[usp_userEmailCheck] @MailId,@AlreadyExistYesNo OUT",
                 new SqlParameter("@MailId", email),
                 AlreadyExistYesNo);
            if (Convert.ToInt32(AlreadyExistYesNo.Value) > 0)
                return 1;
            else
                return 0;
        }

        public async Task<int> ClientChangePassword(string email, string NewPassword, string OldPassword)
        {
                var ctx = new LMSDB_DevContext();
                var Status = new SqlParameter("@Status", SqlDbType.Int);
                @Status.Direction = ParameterDirection.Output;
                ctx.Database.ExecuteSqlCommand("[dbo].[usp_clientChangePassword] @MailId,@NewPassword,@OLdPassword,@Status OUT",
                     new SqlParameter("@MailId", email),
                     new SqlParameter("@NewPassword", NewPassword),
                     new SqlParameter("@OLdPassword", OldPassword),
                     @Status);
                if (Convert.ToInt32(@Status.Value) == 1)
                    return 1;
                else
                    return 0;
        }
        public int GetEmailCount(string email)
        {
                var ctx = new LMSDB_DevContext();
                var Count = new SqlParameter("@Count", SqlDbType.Int);
                @Count.Direction = ParameterDirection.Output;
                ctx.Database.ExecuteSqlCommand("[dbo].[usp_CheckUserEmailCount] @MailId,@Count OUT",
                     new SqlParameter("@MailId", email),
                     @Count);
                if(Convert.ToInt32(@Count.Value) == 1)
                    return 1;
                else
                    return 0;
        }

        #region AddClient
        public int RegisterClient(ClientDto ClientDto)
        {
            
            var ctx = new LMSDB_DevContext();
            var clientId = new SqlParameter("@ClientId", SqlDbType.Int);
            clientId.Direction = ParameterDirection.Output;
            ClientDto.Active = false;
            int emailCount=GetEmailCount(ClientDto.Email);
            if (emailCount == 0)
            {
                int status = ctx.Database.ExecuteSqlCommand("[dbo].[usp_insert_client] @FirstName,@LastName,@CompanyName,@EMail,@phoneNumber,@Photo,@NumberOfStores,@Active,@CreatedByUserId,@ModifiedByUserId,@ClientId OUT",
                    new SqlParameter("@FirstName", ClientDto.FirstName),
                    new SqlParameter("@LastName", ClientDto.LastName),
                    new SqlParameter("@CompanyName", ClientDto.CompanyName),
                    new SqlParameter("@EMail", ClientDto.Email),
                    new SqlParameter("@phoneNumber", ClientDto.PhoneNumber),
                    new SqlParameter("@Photo", ClientDto.Photo),
                    new SqlParameter("@NumberOfStores", ClientDto.NumberOfStores),
                    new SqlParameter("@Active", ClientDto.Active),
                    new SqlParameter("@CreatedByUserId", ClientDto.CreatedByUserId),
                    new SqlParameter("@ModifiedByUserId", ClientDto.ModifiedByUserId),
                    clientId);
                if (status == 1)
                {
                    var passwordgenStatus = new SqlParameter("@Status", SqlDbType.Int);
                    passwordgenStatus.Direction = ParameterDirection.Output;
                    string password = Utility.generateOTP();
                    int st = ctx.Database.ExecuteSqlCommand("usp_client_generatepassword @Client_id,@Password,@Active,@CreatedByUserId,@ModifiedByUserId,@Status OUT",
                  new SqlParameter("@Client_id", Convert.ToInt32(clientId.Value)),
                  new SqlParameter("@Password", password),
                  new SqlParameter("@Active", false),
                  new SqlParameter("@CreatedByUserId", ClientDto.CreatedByUserId),
                  new SqlParameter("@ModifiedByUserId", ClientDto.ModifiedByUserId),
                  passwordgenStatus);
                    List<string> storeCodes = new List<string>();
                    for (int i = 0; i < ClientDto.NumberOfStores; i++)
                    {
                        var storeInsertStatus = new SqlParameter("@Status", SqlDbType.Int);
                        storeInsertStatus.Direction = ParameterDirection.Output;
                        string storecode = Utility.GenerateStoreCode(3);
                        ctx.Database.ExecuteSqlCommand("usp_client_insertstores @Client_id,@ClientStoreCode,@CreatedByUserId,@ModifiedByUserId,@Status OUT",
                        new SqlParameter("@Client_id", Convert.ToInt32(clientId.Value)),
                        new SqlParameter("@ClientStoreCode", storecode),
                        new SqlParameter("@CreatedByUserId", ClientDto.CreatedByUserId),
                        new SqlParameter("@ModifiedByUserId", ClientDto.ModifiedByUserId),
                        storeInsertStatus);
                        storeCodes.Add(storecode);
                    }
                    if (Convert.ToInt32(clientId.Value) > 0 && Convert.ToInt32(passwordgenStatus.Value) > 0)
                    {
                        sendEmailToClient(ClientDto.Email, password, storeCodes);
                        return Convert.ToInt32(clientId.Value);
                    }
                    else
                        return 0;
                }
                else
                    return 0;
            }
            else
            {
                return -1;
            }
        }
        #endregion

        public int ClientLogin(ClientLoginDto ClientLoginDto)
        {
            var ctx = new LMSDB_DevContext();
            var status = new SqlParameter("@Status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            var AlreadyExistYesNo = new SqlParameter("@AlreadyExistYesNo", SqlDbType.Int);
            AlreadyExistYesNo.Direction = ParameterDirection.Output;

            ctx.Database.ExecuteSqlCommand("[dbo].[usp_userEmailCheck] @MailId,@AlreadyExistYesNo OUT",
                 new SqlParameter("@MailId", ClientLoginDto.Email),
                 AlreadyExistYesNo);
            if (Convert.ToInt32(AlreadyExistYesNo.Value) > 0)
            {
                ctx.Database.ExecuteSqlCommand("[dbo].[usp_clientlogincheck] @Email,@Password,@StoreCode,@status OUT",
                new SqlParameter("@Email", ClientLoginDto.Email),
                new SqlParameter("@Password", ClientLoginDto.Password),
                new SqlParameter("@StoreCode", ClientLoginDto.StoreCode),
                 status);
                return Convert.ToInt32(status.Value);
            }
            else
            {
                return 3;
            }


        }

        #region GetClientStoreDetails
        public string GetClientStoreDetails(string eMail)
        {
            var ctx = new LMSDB_DevContext();
            var count = new SqlParameter("@Count", SqlDbType.Int);
            count.Direction = ParameterDirection.Output;
            var StoresDetails = new SqlParameter("@StoresDetails", SqlDbType.VarChar);
            StoresDetails.Direction = ParameterDirection.Output;
            StoresDetails.Size = 100;

            ctx.Database.ExecuteSqlCommand("[dbo].[usp_CheckClientStoresCount] @Email,@Count OUT",
                 new SqlParameter("@Email", eMail),
                 count);

            if(Convert.ToInt32(count.Value)>=1)
            {
                ctx.Database.ExecuteSqlCommand("[dbo].[usp_GetStoresDetailsByClient] @Email,@StoresDetails OUT",
               new SqlParameter("@Email", eMail),
               StoresDetails);

                if(StoresDetails.Value.ToString().Length>0)
                {
                    return StoresDetails.Value.ToString();
                }
                else
                {
                    return string.Empty;
                }
            }
            return string.Empty;

            
        }
        #endregion
    }
}
