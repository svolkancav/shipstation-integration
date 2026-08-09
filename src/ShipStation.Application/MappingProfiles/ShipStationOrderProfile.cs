using System.Text.Json;
using AutoMapper;
using ShipStation.Application.Models;
using ShipStation.Core.Entities;

namespace ShipStation.Application.MappingProfiles;

public class ShipStationOrderProfile : Profile
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public ShipStationOrderProfile()
    {
        CreateMap<OrderModel, ShipStationOrder>()
            // The wire model carries an offset; the column stores a UTC instant.
            .ForMember(entity => entity.OrderDate,
                options => options.MapFrom(model => model.OrderDate.UtcDateTime))
            .ForMember(entity => entity.ModifyDate,
                options => options.MapFrom(model => model.ModifyDate.HasValue
                    ? model.ModifyDate.Value.UtcDateTime
                    : (DateTime?)null))
            .ForMember(entity => entity.Payload,
                options => options.MapFrom(model => JsonSerializer.Serialize(model, PayloadOptions)))
            // Stamped by the sync service so every record in a run shares one value.
            .ForMember(entity => entity.SyncedAt, options => options.Ignore());
    }
}
