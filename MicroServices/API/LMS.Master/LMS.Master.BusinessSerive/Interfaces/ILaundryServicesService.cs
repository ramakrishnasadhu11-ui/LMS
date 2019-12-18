using LMS.Master.DTO.Entities.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Master.BusinessSerive.Interfaces
{
    public interface ILaundryServicesService
    {
        int AddLaundryServices(LaundryServicesDto LaundryServicesDto);
    }
}
