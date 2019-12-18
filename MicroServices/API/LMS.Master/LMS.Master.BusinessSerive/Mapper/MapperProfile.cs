using AutoMapper;
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

          //  CreateMap<UserGender, UserGenderDto>().ReverseMap();
            //   CreateMap<SurveyDataDto, SurveyData>().ReverseMap();
        }
    }
}
