# Time Zone Mapping

DurableStack standardizes recurring job schedules on IANA time zone IDs.

Use these IDs in registration APIs and configuration values for recurring jobs.

Examples:

- `UTC`
- `America/Chicago`
- `America/New_York`

Why IANA:

- portable across .NET, Node.js/TypeScript, Java, Go, and Linux-native tooling
- avoids platform-specific Windows-only naming differences
- simplifies cross-runtime operational consistency

## IANA to Windows reference

Use this table when migrating existing schedules from Windows IDs.

| IANA ID | Windows ID |
| --- | --- |
| UTC | UTC |
| America/Chicago | Central Standard Time |
| America/New_York | Eastern Standard Time |
| America/Denver | Mountain Standard Time |
| America/Los_Angeles | Pacific Standard Time |
| America/Phoenix | US Mountain Standard Time |
| America/Anchorage | Alaskan Standard Time |
| Pacific/Honolulu | Hawaiian Standard Time |
| Europe/London | GMT Standard Time |
| Europe/Paris | Romance Standard Time |
| Europe/Berlin | W. Europe Standard Time |
| Europe/Madrid | Romance Standard Time |
| Europe/Rome | W. Europe Standard Time |
| Europe/Amsterdam | W. Europe Standard Time |
| Europe/Stockholm | W. Europe Standard Time |
| Europe/Warsaw | Central European Standard Time |
| Europe/Istanbul | Turkey Standard Time |
| Asia/Dubai | Arabian Standard Time |
| Asia/Kolkata | India Standard Time |
| Asia/Bangkok | SE Asia Standard Time |
| Asia/Singapore | Singapore Standard Time |
| Asia/Hong_Kong | China Standard Time |
| Asia/Tokyo | Tokyo Standard Time |
| Asia/Seoul | Korea Standard Time |
| Australia/Sydney | AUS Eastern Standard Time |
| Australia/Perth | W. Australia Standard Time |
| Pacific/Auckland | New Zealand Standard Time |
| Africa/Johannesburg | South Africa Standard Time |
| America/Sao_Paulo | E. South America Standard Time |
| America/Bogota | SA Pacific Standard Time |
| America/Mexico_City | Central Standard Time (Mexico) |

Notes:

- some Windows IDs map to multiple IANA zones; choose the IANA zone that matches your locality and DST rules
- prefer region-specific IANA IDs over legacy aliases for clarity
