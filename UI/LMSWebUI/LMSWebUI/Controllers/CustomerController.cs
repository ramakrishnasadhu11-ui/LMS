using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMSClientFactory.Helper;
using LMSWebUI.Models;
using LMSWebUI.Models.Customer;
using LMSWebUI.Models.Login;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace LMSWebUI.Controllers
{
    public class CustomerController : Controller
    {
       private readonly IHttpClientApi clientAPI;
        public CustomerController(IHttpClientApi clientAPI)
        {
            this.clientAPI = new HttpClientApi("https://localhost:44352/Customer");
          //  this.clientAPI = clientAPI;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> AddCustomer(CustomerInfoDto CustomerInfoDto)
        { 
            if(!!string.IsNullOrWhiteSpace(CustomerInfoDto.CustomerName))
            { 
            TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter Customer Name" });
            return RedirectToAction("Index","Customer");
            }
             if(!!string.IsNullOrWhiteSpace(CustomerInfoDto.Address))
            { 
            TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter Customer Address" });
            return RedirectToAction("Index","Customer");
            }
            string tenantName=HttpContext.Session.GetString("TenantName");
            string tenanStore=HttpContext.Session.GetString("TenantStore");
            CustomerInfoDto.StoreCode=tenanStore;
            CustomerInfoDto.TenantName=tenantName;

             CustomerIOResponse responseMessage=new CustomerIOResponse();
            try
            {
            if(CustomerInfoDto!=null)
            {
                    responseMessage = await clientAPI.SendRequestAsync<CustomerIOResponse>("/InsertCustomer", CustomerInfoDto, RestSharp.Method.POST);
                    if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                         TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert-success", Title = "Success!", DisplayMessage = responseMessage.Message });
                         return RedirectToAction("Index","Customer");
                    }
            }
            else
            {
                    TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert alert-warning", Title = "Fail!", DisplayMessage = responseMessage.Message });
                    return RedirectToAction("Index","Customer");           
            }
            TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert alert-danger", Title = "Fail!", DisplayMessage = responseMessage.Message });
                 return RedirectToAction("Index","Customer");           
            }
            catch(Exception ex)
            {
                 TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert alert-danger", Title = "Fail!", DisplayMessage = responseMessage.Message });
                 return RedirectToAction("Index","Customer");
            }
        }
    }
}