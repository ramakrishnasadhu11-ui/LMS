using AutoMapper;
using LMS.Master.DataModels.Entities;
using LMS.Master.DTO.Entities.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Master.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;
            CreateMap<AvailableLaundryServicesForClient, AvailableLaundryServicesForClientDto>().ReverseMap();
            CreateMap<AvailableLaundryCustomerTypesForClient, AvailableLaundryCustomerTypesForClientDto>().ReverseMap();
            //  CreateMap<UserGender, UserGenderDto>().ReverseMap();
            //   CreateMap<SurveyDataDto, SurveyData>().ReverseMap();
        }
    }
}
