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

        #region AddClient
        public int RegisterUser(CustomerDto CustomertDto)
        {
           
             var ctx = new LMSDB_DevContext();
            var userId = new SqlParameter("@ClientId", SqlDbType.Int);
            userId.Direction = ParameterDirection.Output;
            CustomertDto.Active = false;
                 int status =ctx.Database.ExecuteSqlCommand("usp_insert_client @ClientGenderID,@ClientNam,@phoneNumber,@EMail,@MiddleName,@FamilyName,@Photo,@Address1,@Address2,@City,@Region,@Zip,@Country,@Active,@CreatedByUserId,@ModifiedByUserId,@ClientId OUT",
                 new SqlParameter("@ClientGenderID", CustomertDto.CustomerGenderId),
                 new SqlParameter("@ClientNam", CustomertDto.CustomerName),
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
                 userId);

            if (status == 1)
            {
                string password = string.Empty;
                password = Utility.encode(CustomertDto.Email);
                var passwordgenStatus = new SqlParameter("@Status", SqlDbType.Int);
                passwordgenStatus.Direction = ParameterDirection.Output;
                 int st = ctx.Database.ExecuteSqlCommand("usp_client_generatepassword @User_id,@Password,@Active,@CreatedByUserId,@ModifiedByUserId,@Status OUT",
                 new SqlParameter("@User_id", Convert.ToInt32(userId.Value)),
                 new SqlParameter("@Password", password),
                 new SqlParameter("@Active", 1),
                 new SqlParameter("@CreatedByUserId", CustomertDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", CustomertDto.ModifiedByUserId),
                 passwordgenStatus);
                if (Convert.ToInt32(userId.Value) > 0 && Convert.ToInt32(passwordgenStatus.Value) > 0)
                {
                   sendEmailToUser(CustomertDto.Email, password);
                    return Convert.ToInt32(userId.Value);
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
            try
            { 
            var ctx = new LMSDB_DevContext();
            string password = string.Empty;
            password = Utility.encode(email);

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
            catch(Exception ex)
            {
                throw ex;
            }
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

        public async Task<int> ChangePassword(string email, string NewPassword, string OldPassword)
        {
                var ctx = new LMSDB_DevContext();
                var Status = new SqlParameter("@Status", SqlDbType.Int);
                @Status.Direction = ParameterDirection.Output;
                ctx.Database.ExecuteSqlCommand("[dbo].[usp_userChangePassword] @MailId,@NewPassword,@OLdPassword,@Status OUT",
                     new SqlParameter("@MailId", email),
                     new SqlParameter("@NewPassword", NewPassword),
                     new SqlParameter("@OLdPassword", OldPassword),
                     @Status);
                if (Convert.ToInt32(@Status.Value) == 1)
                    return 1;
                else
                    return 0;
        }
        public async Task<int> GetEmailCount(string email)
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

    }
}
