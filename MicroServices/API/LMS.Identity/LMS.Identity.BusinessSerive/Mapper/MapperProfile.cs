using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using LMS.Identity.DataModels;
using LMS.Identity.DTO.Entities.Dto;

namespace LMS.Identity.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;

            CreateMap<UserGender, UserGenderDto>().ReverseMap();
         //   CreateMap<SurveyDataDto, SurveyData>().ReverseMap();
        }
    }
}
