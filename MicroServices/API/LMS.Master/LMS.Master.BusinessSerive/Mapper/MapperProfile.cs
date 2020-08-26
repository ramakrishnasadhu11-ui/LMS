using AutoMapper;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;

namespace LMS.Master.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;
            CreateMap<CustomerEntity, CustomerDto>().ReverseMap();
        }
    }
}
