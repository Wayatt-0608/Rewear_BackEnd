using REWEAR.Domain.Entities;
namespace REWEAR.Application.DTOs;
public sealed class RequestReturnDto { public string Reason { get; set; } = ""; }
public sealed class ReceiveReturnDto { public string Reference { get; set; } = ""; public string Reason { get; set; } = ""; }
public sealed class InspectReturnDto { public List<ReturnItemDecision> Items { get; set; } = new(); }
