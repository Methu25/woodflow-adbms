namespace WoodFlow.Application.DTOs;

public sealed record HealthResponse(string ApplicationStatus, string DatabaseStatus);
