using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Login
{
    public class TenantDto
    {
       [Required(ErrorMessage = "Number of stores is required.")]
       [RegularExpression("^[1-9]\\d*$", ErrorMessage = "Number of stores must be a whole number greater than 0.")]
       public string NoOfStores { get; set; }
       [Required(ErrorMessage = "Company name is required.")]
       [RegularExpression("^(?=.*[A-Za-z0-9&.,'\\-()])[A-Za-z0-9&.,'\\-() ]+$", ErrorMessage = "Please enter a valid company name.")]
       public string TenantName { get; set; }
       [Required(ErrorMessage = "Email address is required.")]
       [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
       public string Email {get;set;}
       [Required(ErrorMessage = "Phone number is required.")]
       [RegularExpression("^\\d+$", ErrorMessage = "Please enter a valid phone number.")]
       public string PhoneNumber { get; set; }
       [Required(ErrorMessage = "Last name is required.")]
       [RegularExpression("^\\s*[A-Za-z]+(?:\\s+[A-Za-z]+)*\\s*$", ErrorMessage = "Last name can contain letters and spaces only.")]
       public string MiddleName { get; set; }
       [Required(ErrorMessage = "First name is required.")]
       [RegularExpression("^\\s*[A-Za-z]+(?:\\s+[A-Za-z]+)*\\s*$", ErrorMessage = "First name can contain letters and spaces only.")]
       public string FamilyName { get; set; }
       public string Address { get; set; }
       public string City { get; set; }
       public string Region { get; set; }
       public string Zip { get; set; }
       public string Country { get; set; }
       public string Message{get;set;}
    }
}
