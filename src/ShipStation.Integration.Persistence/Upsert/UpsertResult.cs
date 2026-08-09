namespace ShipStation.Integration.Persistence.Upsert;

/// <summary>
/// Outcome of an add-or-update run.
/// </summary>
/// <param name="Inserted">Rows that did not exist before.</param>
/// <param name="Updated">Rows that existed and differed.</param>
/// <param name="Unchanged">
/// Rows that existed and matched, so nothing was written. On a steady-state re-sync
/// this should be the overwhelming majority; if it is not, change detection is not
/// working and the job is rewriting rows for nothing.
/// </param>
/// <param name="DuplicatesCollapsed">
/// Rows dropped because the same key appeared more than once in the input.
/// </param>
public readonly record struct UpsertResult(int Inserted, int Updated, int Unchanged, int DuplicatesCollapsed)
{
    public int Affected => Inserted + Updated;

    public int Total => Inserted + Updated + Unchanged;

    public static UpsertResult operator +(UpsertResult left, UpsertResult right) => new(
        left.Inserted + right.Inserted,
        left.Updated + right.Updated,
        left.Unchanged + right.Unchanged,
        left.DuplicatesCollapsed + right.DuplicatesCollapsed);

    public override string ToString() =>
        $"{Inserted} inserted, {Updated} updated, {Unchanged} unchanged" +
        (DuplicatesCollapsed > 0 ? $", {DuplicatesCollapsed} duplicates collapsed" : string.Empty);
}
