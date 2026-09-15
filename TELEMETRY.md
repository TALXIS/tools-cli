# Telemetry

TALXIS CLI (`txc`) collects usage data to help improve the tool and understand how it is used. This document describes what data is collected and how to manage it.

## Data Collected

When telemetry is active, `txc` reports the following to [Azure Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview):

| Category | Examples |
|----------|---------|
| **Command execution** | Command name, duration, exit code, error messages |
| **Environment context** | Operating system, CLI version, entry point (cli / mcp), session ID |
| **Authenticated user context** | User principal name (UPN), Entra object ID, tenant ID, environment URL *(only when a profile is active)* |
| **HTTP dependencies** | Outbound request URL, status code, duration (Dataverse, NuGet, etc.) |

Machine name and OS description are included as part of the OpenTelemetry resource attributes.

## Why

Telemetry helps the maintainer:

- Identify which commands are used most and where errors occur
- Understand which environments and organizations use the tool
- Prioritize features and allocate development effort
- Diagnose issues reported by users (session ID correlation)

## Who Processes the Data

| Role | Entity |
|------|--------|
| **Data controller** | NETWORG CZ s.r.o., Prague, Czech Republic |
| **Data processor** | Microsoft (Azure Application Insights) |

Data is stored in an Azure Application Insights workspace operated by NETWORG CZ s.r.o.

## Legal Basis

Processing is based on **legitimate interest** (Art. 6(1)(f) GDPR). The tool is provided free of charge; understanding usage patterns is a proportionate interest that enables continued development and improvement.

## Opting Out

Set the environment variable before running `txc`:

```sh
export TXC_TELEMETRY_OPTOUT=1
```

Or set it in the CLI configuration:

```sh
txc config setting set telemetry.optOut true
```

When opted out, no telemetry data is collected or transmitted. The session ID is still resolved locally for log correlation but is not sent to any external service.

## Enterprise Override

Organizations that prefer to collect telemetry in their own Application Insights instance can redirect it:

```sh
export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=..."
```

Or via the CLI configuration:

```sh
txc config setting set telemetry.connectionString "InstrumentationKey=..."
```

When a custom connection string is set, all telemetry flows to the specified instance instead of the default.

## Data Retention

Data is retained according to the Application Insights workspace configuration (default: 90 days).

## Your Rights

Under GDPR you have the right to request access to, rectification of, or erasure of your data, as well as the right to object to processing. Contact **hello@networg.com** with your request.

## More Information

- [Azure Application Insights data collection](https://learn.microsoft.com/azure/azure-monitor/app/data-retention-privacy)
- [GDPR — Regulation (EU) 2016/679](https://eur-lex.europa.eu/eli/reg/2016/679/oj)
