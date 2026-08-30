namespace LifeOs.Infrastructure.Diagnostics;

/// <summary>
/// One diagnostic: a plain SQL query that fires against the kernel's own data
/// and the human-readable rule it represents. Discovered from an embedded
/// <c>NN__&lt;slug&gt;.sql</c> file (see <c>db/diagnostics/README.md</c> for the
/// result contract the SQL must satisfy).
/// </summary>
/// <param name="Name">Stable slug, from the filename; the value <c>--only</c> matches.</param>
/// <param name="Title">Human rule statement, from the file's <c>-- title:</c> header (or the slug).</param>
/// <param name="Order">Ordering prefix from the filename; controls report order.</param>
/// <param name="Sql">The query. Returns one row per finding per the result contract.</param>
public sealed record Diagnostic(string Name, string Title, long Order, string Sql);
