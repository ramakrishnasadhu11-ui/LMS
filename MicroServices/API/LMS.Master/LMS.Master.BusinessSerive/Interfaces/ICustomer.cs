using LMS.Master.DTO;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;


namespace LMS.Master.BusinessSerive.Interfaces
{
    public interface ICustomer
    {
        Task<ActionReturnType> Register(CustomerDto customerDto);
    }
}
