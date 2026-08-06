namespace ForgeConflict.Core.Safety;

/// <summary>
/// Applique et defait une modification unitaire. Separe du moteur transactionnel
/// pour que la logique de surete soit testable sans jamais toucher au systeme.
/// </summary>
public interface IChangeApplier
{
    void Apply(ChangeRecord change);

    void Revert(ChangeRecord change);
}
