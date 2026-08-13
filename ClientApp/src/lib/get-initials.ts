export function getInitial (name?: string): string
{
    if (!name || name.trim()==="") return "?";

    const parts = name.trim().split(/\s+/);

    if (parts.length === 1)
    {
        return parts[0].substring(0,2).toUpperCase();

    }
    const fist = parts[0][0];
    const last = parts[parts.length -1][0];
    return (fist + last).toUpperCase();
}