namespace ForgeConflict.Core.Safety;

public enum TransactionState
{
    Ouverte,
    Validee,
    Restauree,
}

/// <summary>
/// Niveau de risque d'une ACTION, a ne pas confondre avec la severite d'un CONSTAT.
/// Voir section 6.0 du document de conception.
/// </summary>
public enum RiskLevel
{
    Faible,
    Moyen,
    Eleve,
    Critique,
}

/// <summary>
/// Unite de modification reversible. Son plan de restauration est ecrit sur disque
/// AVANT que la moindre modification soit appliquee : une transaction retrouvee
/// <see cref="TransactionState.Ouverte"/> au demarrage signale une interruption et
/// declenche le retour arriere.
/// </summary>
public sealed record Transaction(
    Guid Id,
    string RuleId,
    string Title,
    RiskLevel Risk,
    TransactionState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ChangeRecord> Changes)
{
    public bool NeedsRecovery => State == TransactionState.Ouverte;

    public bool AffectsDisplay => Changes.Any(c => c.AffectsDisplay);

    public static Transaction Open(
        string ruleId,
        string title,
        RiskLevel risk,
        IReadOnlyList<ChangeRecord> changes,
        DateTimeOffset? openedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            throw new ArgumentException(
                "Une transaction sans changement n'a rien a restaurer.", nameof(changes));
        }

        return new Transaction(
            Guid.NewGuid(), ruleId, title, risk, TransactionState.Ouverte,
            openedAt ?? DateTimeOffset.Now, null, changes);
    }

    public Transaction Commit(DateTimeOffset at) =>
        this with { State = TransactionState.Validee, ClosedAt = at };

    public Transaction RolledBack(DateTimeOffset at) =>
        this with { State = TransactionState.Restauree, ClosedAt = at };
}
