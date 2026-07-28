namespace Comprexy.Application.Abstractions;

/// <summary>Creates an owned map UoW; caller must <c>await using</c> / dispose.</summary>
public interface IToolIrCallIdMapUnitOfWorkFactory
{
    IToolIrCallIdMapUnitOfWork Create();
}
