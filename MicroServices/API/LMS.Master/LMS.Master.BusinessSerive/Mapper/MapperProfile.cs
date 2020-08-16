using AutoMapper;

namespace LMS.Master.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;
            //CreateMap<AvailableLaundryServicesForClient, AvailableLaundryServicesForClientDto>().ReverseMap();
            //CreateMap<AvailableLaundryCustomerTypesForClient, AvailableLaundryCustomerTypesForClientDto>().ReverseMap();
            ////  CreateMap<UserGender, UserGenderDto>().ReverseMap();
            //   CreateMap<SurveyDataDto, SurveyData>().ReverseMap();
        }
    }
}
