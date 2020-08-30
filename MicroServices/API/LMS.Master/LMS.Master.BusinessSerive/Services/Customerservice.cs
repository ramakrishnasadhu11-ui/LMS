using AutoMapper;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using LMS.Master.Utilities;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LMS.Master.BusinessSerive.Services
{
    public class Customerservice : ICustomer
    {
         private IMapper _mapper { get; }
        private readonly MongoClientSettings _tenantMongoClientSettings = null;
        private readonly IMongoDatabase _tenantCustomersDatabase = null;
        private readonly IMongoCollection<CustomerEntity> _customerEntity;
        private readonly ITenantCustomerRegistryConnection _tenantCustomerRegistryConnection;
        //private readonly IMailConfiguration _mailConfiguration;
        //private readonly string _mailSubject,_mailFrom,_sender,_smtpServer,_reciever,_username,_password,_apikey;
        //private readonly int _port;
        public Customerservice(IHttpContextAccessor httpContextAccessor,ITenantCustomerRegistryConnection tenantCustomerRegistryConnection,IMapper mapper)
        {
              _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _tenantCustomerRegistryConnection = tenantCustomerRegistryConnection;
            _tenantMongoClientSettings=MongoClientSettings.FromUrl(new MongoUrl(_tenantCustomerRegistryConnection.ConnectionString));
            _tenantMongoClientSettings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            _tenantMongoClientSettings.ConnectTimeout = new System.TimeSpan(_tenantCustomerRegistryConnection.ConnectTimeoutInSeconds * System.TimeSpan.TicksPerSecond);
            var tenantRegisterClient = new MongoClient(_tenantMongoClientSettings);
            if (tenantRegisterClient != null)
                _tenantCustomersDatabase = tenantRegisterClient.GetDatabase(_tenantCustomerRegistryConnection.DatabaseName);
             _customerEntity = _tenantCustomersDatabase.GetCollection<CustomerEntity>(_tenantCustomerRegistryConnection.TenantCustomersCollectionName);
        }

          public ActionReturnType GetCustomers()
        {
                var result = _customerEntity.Find(new BsonDocument()).ToList();
                List<string> listnames=new List<string>(); 
                foreach(CustomerEntity doc in result)
                 {
                 listnames.Add(doc.CustomerName);
                 }    
                if (result!=null)
                 {
                     return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse {StatusCode="200", CustomerNames=listnames});
                 }
             return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new CustomerIOResponse {StatusCode="500", Message = CustomerValidationMessage.CUSTOMER_INSERT_ERROR_MESSAGE });
        }
        public async Task<ActionReturnType> AddCustomer(CustomerDto customerDto)
        {
            if(customerDto==null)
            {
                 return ActionSet.ActionReturnType(HttpStatusCode.NoContent, new CustomerIOResponse {StatusCode="204", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }
            else if(customerDto!=null)
            {
                var customerfilter = Builders<CustomerEntity>.Filter.Where(cu => cu.CustomerName == customerDto.CustomerName);
                var result = await _customerEntity.Find(customerfilter).FirstOrDefaultAsync();
                if (result!=null)
                 {
                     return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new CustomerIOResponse {StatusCode="208", Message = CustomerValidationMessage.CUSTOMER_DATAFOUND_ERROR_MESSAGE});
                 }
               var customerData = _mapper.Map<CustomerEntity>(customerDto);
              string customerCode=  GenerateRandomID();
               customerData.CustCode="Cust-"+customerCode;
               customerData.CreatedDate=DateTime.UtcNow;
               customerData.ModifiedDate=DateTime.UtcNow;
               await  _customerEntity.InsertOneAsync(customerData);
              return ActionSet.ActionReturnType(HttpStatusCode.Created, new CustomerIOResponse {CustCode=customerData.CustCode, StatusCode="200", Message = CustomerValidationMessage.CUSTOMER_INSERT_SUCCESS_MESSAGE });
            }
             return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new CustomerIOResponse {StatusCode="500", Message = CustomerValidationMessage.CUSTOMER_INSERT_ERROR_MESSAGE });
        }
          private static string GenerateRandomID()
        {
            string srcrandomId = LMSRandom("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8);
            return srcrandomId;
        }
         public static string LMSRandom(string pattern, int length, bool isPassword = false)
        {
            return GenerateDynamicCryptoString(pattern, length, isPassword);
        }
         public static string GenerateDynamicCryptoString(string pattern, int length, bool isPassword = false)
        {
            if (length != 0)
            {
                RNGCryptoServiceProvider provider = CreateRNGCryptoServiceProvider();
                StringBuilder sb;
                do
                {
                    sb = new StringBuilder();
                    var byteArray = new byte[length > 3 ? length : 4];
                    provider.GetBytes(byteArray);

                    int patternLength = pattern.Length;

                    //Gets character on index based from the pattern passed
                    for (var i = 0; i < byteArray.Length; i++)
                    {
                        byte x = byteArray[i];
                        while (x >= patternLength)
                            x = Convert.ToByte(x % patternLength);
                        sb.Append(pattern[x]);
                    }

                    //Below condition checks if atleast one special character is avaible in generated string.
                    isPassword = HasSpecialCharacters(sb, pattern, isPassword);
                } while (isPassword);

                return sb.ToString();
            }

            return string.Empty;
        }
          private static bool HasSpecialCharacters(StringBuilder sb, string pattern, bool isPassword)
        {
            //Below condition checks if atleast one special character is avaible in generated string.
            if (isPassword)
            {
                var regex = new Regex("[a-zA-Z0-9]*");
                var specialChars = regex.Replace(pattern, "");
                if (!string.IsNullOrEmpty(specialChars))
                {
                    var specialCharsRegex = new Regex("[" + specialChars + "]");
                    isPassword = !specialCharsRegex.IsMatch(sb.ToString());
                }
                else
                    isPassword = false;
            }
            return isPassword;
        }
       private static RNGCryptoServiceProvider CreateRNGCryptoServiceProvider()
        {
            RNGCryptoServiceProvider rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            return rngCryptoServiceProvider;
        }
        public interface ITenantCustomerRegistryConnection
        {
        string ConnectionString { get; set; }
        string DatabaseName { get; set; }
        string TenantCustomersCollectionName { get; set; }
        int ConnectTimeoutInSeconds { get; set; }
        }

         public class TenantCustomerRegistryConnection : ITenantCustomerRegistryConnection
        {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string TenantCustomersCollectionName { get; set; }
        public int ConnectTimeoutInSeconds { get; set; }
        }
    }
}
