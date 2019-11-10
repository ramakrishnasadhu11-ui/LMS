using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
namespace LMS.Identity.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;

         //   CreateMap<Header, ProposalHeaderDto>().ReverseMap();
         //   CreateMap<SurveyDataDto, SurveyData>().ReverseMap();
        }
    }
}
