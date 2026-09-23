# Elevtilgang – TAP for Microsoft 365-elever

Elevtilgang er en enkel ASP.NET Core-webapplikasjon for å la lærere og IT-personell generere **Temporary Access Pass (TAP)** for elever i Microsoft Entra ID.

Løsningen er laget for situasjoner der en elev har problemer med pålogging eller trenger å registrere en ny autentiseringsmetode, uten at lærer eller superbruker trenger tilgang til Microsoft Entra-administrasjon.

Applikasjonen bruker Microsoft Graph og kontrollerer tilgang mot Entra ID-grupper.

## Funksjoner

- Generering av Temporary Access Pass (TAP)
- Innlogging med Microsoft Entra ID
- Tilgangsstyring basert på Entra ID-grupper
- Lærere ser kun elever ved skolene de tilhører
- IT-drift kan gis tilgang til alle skoler
- Elever filtreres mot en egen elevgruppe
- Støtte for flere skoler og klasser
- Filtrering på skole og klasse
- Søk på elevnavn og UPN
- TAP genereres med AJAX uten å laste siden på nytt
- Serversidekontroll av tilgang før hver TAP-generering
- Audit-logg for genererte, avviste og feilede TAP-forsøk
- Egen audit-side kun tilgjengelig for IT-drift
- Caching av gruppemedlemmer for å redusere Microsoft Graph-kall
- Konfigurerbar TAP-levetid, gjenbruk av kode og cachetid

---

## Hvordan tilgangsstyringen fungerer

Løsningen benytter tre hovedtyper grupper:

### IT-drift

Medlemmer av IT-gruppen kan få tilgang til alle elever og alle skoler.

### Lærere

Brukeren må være medlem av den konfigurerte lærergruppen.

I tillegg kontrolleres hvilke skolegrupper læreren er medlem av.

Eksempel:

```text
Lærer
 ├── Teachers
 └── School1
```

Læreren får da kun se elever som tilhører `School1`.

### Elever

En elev må både:

1. være medlem av den konfigurerte `A3Students`-gruppen
2. tilhøre en skole læreren har tilgang til

Klassegrupper brukes i tillegg til å vise og filtrere elevene etter klasse.

---

## Sikkerhet

Tilgangen kontrolleres ikke bare i brukergrensesnittet.

Når en bruker trykker **Generer TAP**, utføres en ny autorisasjonskontroll på serveren før Microsoft Graph får lov til å generere TAP.

Det betyr at det ikke er tilstrekkelig å manipulere `userId` eller HTTP-requesten i nettleseren for å generere TAP for en elev brukeren ikke har tilgang til.

Et ugyldig forsøk returnerer:

```text
403 Forbidden
```

og registreres i auditloggen som `DENIED`.

Data som sendes fra klienten, som elevnavn, skole og klasse, brukes ikke som grunnlag for autorisasjon.

---

## Audit-logg

TAP-hendelser kan logges til en lokal fil.

Eksempel:

```text
2026-08-14 12:03:36 +02:00 | SUCCESS | Operator=teacher@example.no | OperatorOid=... | TargetName=Test Student | TargetUpn=student@example.no | TargetOid=... | School=School1 | Class=9A | Action=GenerateTAP | IP=10.0.0.10
```

Følgende hendelser kan registreres:

- `SUCCESS` – TAP ble generert
- `DENIED` – brukeren forsøkte å generere TAP for en elev utenfor sitt tillatte område
- `ERROR` – det oppstod en feil under TAP-genereringen

**Selve TAP-koden lagres aldri i auditloggen.**

Auditloggen kan vises gjennom applikasjonen, men audit-siden er beskyttet av IT-drift-policyen. Den lokale filen kan være ufullstendig ved drifts-, skrive- eller konfigurasjonsfeil.

---

# Krav

Applikasjonen krever blant annet:

- .NET 8
- ASP.NET Core
- Microsoft Entra ID
- Microsoft Graph
- Entra ID App Registration
- Temporary Access Pass aktivert i tenant
- IIS dersom applikasjonen skal driftes på Windows Server

---

# Microsoft Entra ID App Registration

Opprett en App Registration i Microsoft Entra ID.

Konfigurer redirect URI:

```text
https://SERVER/signin-oidc
```

eller eksempelvis:

```text
https://tap.example.no/signin-oidc
```

Applikasjonen må konfigureres med nødvendige Microsoft Graph-rettigheter for å:

- lese gruppemedlemskap
- lese brukere/grupper som løsningen trenger
- administrere Temporary Access Pass

Administrator consent må gis for nødvendige Application permissions.

> Bruk minst mulig Graph-rettigheter og vurder alltid hvilke permissions som er nødvendige i eget miljø.

---

# Konfigurasjon

Gi `appsettings.example.json` navnet `appsettings.json`, og fyll inn verdiene for deres eget miljø.

Eksempel på innhold i `appsettings.json`:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "YOUR-TENANT-ID",
    "ClientId": "YOUR-CLIENT-ID",
    "ClientSecret": "YOUR-CLIENT-SECRET",
    "CallbackPath": "/signin-oidc"
  },

  "Groups": {
    "ITDrift": "GROUP-ID",
    "Teachers": "GROUP-ID",
    "A3Students": "GROUP-ID",

    "Schools": {
      "School1": {
        "GroupId": "GROUP-ID",

        "Classes": {
          "8A": "GROUP-ID",
          "8B": "GROUP-ID",
          "9A": "GROUP-ID",
          "9B": "GROUP-ID",
          "10A": "GROUP-ID"
        }
      },

      "School2": {
        "GroupId": "GROUP-ID",

        "Classes": {
          "8A": "GROUP-ID",
          "9A": "GROUP-ID",
          "10A": "GROUP-ID"
        }
      }
    }
  },

  "Testing": {
    "IgnoreITDriftBypass": false
  },

  "Cache": {
    "GroupMembersMinutes": 60
  },

  "Audit": {
    "LogFilePath": "C:\\Logger\\tap-audit.log"
  },

  "TAP": {
    "OneTime": {
      "LifetimeMinutes": 60,
      "IsUsableOnce": true
    }
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },

  "AllowedHosts": "*"
}
```

---

## Skoler og klasser

Det kan legges til så mange skoler og klasser som ønskelig.

Eksempel:

```json
"Schools": {
  "School1": {
    "GroupId": "GUID-FOR-SCHOOL1",

    "Classes": {
      "8A": "GUID-FOR-8A",
      "8B": "GUID-FOR-8B",
      "9A": "GUID-FOR-9A",
      "10A": "GUID-FOR-10A"
    }
  }
}
```

`GroupId` er Entra ID Object ID for skolegruppen.

Hver klasse peker tilsvarende på Object ID for klassens Entra ID-gruppe.

---

# Cache

Gruppemedlemmer caches for å redusere antall kall mot Microsoft Graph.

Cachetid konfigureres med:

```json
"Cache": {
  "GroupMembersMinutes": 60
}
```

Eksempelvis betyr `60` at medlemslistene kan ligge i minnet i opptil én time.

Den sikkerhetskritiske autorisasjonskontrollen ved TAP-generering skal fortsatt utføres på serveren og skal ikke baseres på klientens filtrerte elevliste.

---

# TAP

TAP-konfigurasjonen finnes under:

```json
"TAP": {
  "OneTime": {
    "LifetimeMinutes": 60,
    "IsUsableOnce": true
  }
}
```

Dette eksemplet oppretter en TAP som er gyldig i 60 minutter og kan brukes én gang.

`TAP:OneTime:IsUsableOnce` sendes til Microsoft Graph. `true` gir en engangskode; `false` lar samme kode brukes flere ganger innenfor gyldighetstiden. Hvis innstillingen mangler, brukes `true` som standard.

---

# Audit-fil

Auditfilen kan eksempelvis plasseres her:

```text
C:\Logger\tap-audit.log
```

IIS Application Pool-identiteten må ha skrivetilgang til katalogen.

Eksempel:

```text
SYSTEM                  Full Control
Administrators          Full Control
IIS AppPool\<AppPool>   Modify
```

Auditfilen bør ligge **utenfor webroot**, slik at den ikke kan lastes ned direkte via nettstedet.

---

# IIS

Applikasjonen kan publiseres med:

```powershell
dotnet publish -c Release
```

Installer korrekt ASP.NET Core Hosting Bundle på IIS-serveren.

Opprett deretter:

1. IIS Application Pool
2. nettsted/applikasjon
3. HTTPS-binding
4. nødvendige NTFS-rettigheter
5. redirect URI i Entra ID App Registration

Eksempel på produksjonsadresse:

https://tap.example.no

# Ansvarsfraskrivelse

Dette prosjektet håndterer autentiseringsdata og kan generere Temporary Access Pass for brukere i Microsoft Entra ID.

Før løsningen tas i produksjon bør den gjennomgås og testes i eget miljø, inkludert:

- Entra ID-grupper og medlemskap
- Graph permissions
- autorisasjonskontroller
- TAP-policy
- audit-logging
- IIS- og NTFS-rettigheter
- håndtering av secrets

Bruk av løsningen skjer på eget ansvar.

---
