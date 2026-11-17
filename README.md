# Elevtilgang – TAP og enhetsadministrasjon for Microsoft 365-elever

Elevtilgang er en ASP.NET Core-webapplikasjon som lar lærere, superbrukere og IT-personell hjelpe elever med pålogging og tilknyttede skole-PC-er.

Løsningen kan generere **Temporary Access Pass (TAP)** i Microsoft Entra ID, vise Intune-enheter og gi autorisert personell tilgang til synkronisering, wipe og BitLocker-nøkler. Brukeren trenger ikke tilgang til administrasjonsportalene for å utføre disse oppgavene.

Applikasjonen bruker Microsoft Graph og styrer tilgangen gjennom Entra ID-grupper.

## Funksjoner

- Innlogging med Microsoft Entra ID.
- Elevoversikt med filtrering på skole og klasse, søk på navn og UPN samt serienumre.
- Elevfiltre som huskes i nettleserøkten.
- TAP-generering uten å laste siden på nytt, med konfigurerbar levetid og engangsbruk eller gjenbruk.
- Elevdetaljer med tilknyttede Intune-enheter, serienummer, modell og operativsystem.
- Enhetshelse med samsvarsstatus, kryptering, lagringsplass og siste Intune-kontakt.
- Forespørsel om synkronisering av enhet.
- Wipe-forespørsel med bekreftelse og valg om brukerdata skal beholdes.
- Visning av BitLocker-nøkkelmetadata og uthenting av valgt gjenopprettingsnøkkel.
- Auditlogg for TAP og administrative enhetshandlinger, med enhets- og serienummerinformasjon.
- Egen audit-side for IT-drift.
- Caching av gruppemedlemmer for å redusere Graph-kall.

**Wipe og synkronisering er forespørsler til Intune.** At en forespørsel er sendt, betyr ikke at handlingen er fullført på enheten. Wipe er en destruktiv handling; kontroller elev og enhet før bekreftelse.

## Roller og tilgang

| Funksjon | Lærer | Superbruker | IT-drift |
| --- | --- | --- | --- |
| Se elever og elevdetaljer | Egne skoler | Egne skoler | Alle konfigurerte skoler |
| Generere TAP | Egne skoler | Egne skoler | Alle skoler |
| Se tilknyttede enheter | Egne skoler | Egne skoler | Alle skoler |
| Synkronisere enhet | Nei | Egne skoler | Alle skoler |
| Sende wipe-forespørsel | Nei | Egne skoler | Alle skoler |
| Hente BitLocker-nøkkel | Nei | Egne skoler | Alle skoler |
| Åpne samlet audit-side | Nei | Nei | Ja |

Tilgangen bygger på følgende konfigurerte grupper:

- `Groups:ITDrift`: IT-personell med tilgang på tvers av skoler.
- `Groups:Teachers`: lærere.
- `Groups:Superbrukere`: superbrukere med administrative enhetsfunksjoner ved egne skoler.
- `Groups:A3Students`: elevgruppen som avgrenser hvilke elever løsningen håndterer.
- `Groups:Schools`: skolegrupper og tilhørende klassegrupper.

En lærer eller superbruker må være medlem av sin rollegruppe og minst én konfigurert skolegruppe. Superbrukere trenger ikke i tillegg medlemskap i lærergruppen.

Eleven må være medlem av elevgruppen. For lærere og superbrukere må eleven også tilhøre en skole brukeren har tilgang til. Klassegruppene brukes til visning og filtrering.

## Sikkerhet

Tilgangen kontrolleres på serveren før handlingen utføres. Skjulte knapper og filtrering i nettleseren er ikke autorisasjon.

For enhetsoperasjoner kontrolleres både tilgangen til eleven og koblingen mellom elevens UPN og Intune-enheten. Ved BitLocker-oppslag kontrolleres også koblingen mellom enhetens Entra-enhets-ID og den valgte gjenopprettingsnøkkelen.

POST-endepunktene bruker antiforgery-validering. BitLocker-responsene bruker `no-store` og `no-cache`, og nøkkelen fjernes fra visningen når dialogen lukkes. TAP-koder og BitLocker-nøkkelverdier skrives ikke til auditloggen og lagres ikke i `localStorage` eller `sessionStorage`.

Produksjonskonfigurasjon, logger, hemmeligheter og elevdata skal ikke legges i Git. `appsettings.json` er utelatt fra repositoryet og ignorert av Git.

## Krav

- .NET 8 SDK for å bygge prosjektet.
- ASP.NET Core/.NET 8-kjøremiljø på verten.
- Microsoft Entra ID med en App Registration.
- Microsoft Graph-tilgang med administratorgodkjente applikasjonsrettigheter.
- Temporary Access Pass aktivert for aktuelle elever i tenantens autentiseringspolicy.
- Microsoft Intune og administrerte enheter for enhetsfunksjonene.
- Registrerte BitLocker-gjenopprettingsnøkler i Entra ID for nøkkeloppslag.
- IIS og passende ASP.NET Core Hosting Bundle ved drift på Windows/IIS.

## Microsoft Entra ID og Graph

Opprett en App Registration og konfigurer en Web redirect URI som samsvarer med applikasjonens adresse og callback-sti, for eksempel:

```text
https://tap.example.no/signin-oidc
```

Fyll inn tenant-ID, klient-ID og klienthemmelighet i lokal konfigurasjon eller en egnet secrets-løsning.

Innlogging skjer med Microsoft Entra ID. GraphService bruker applikasjonens credentials til Graph-kall; sluttbrukerens rettigheter i løsningen avgrenses av applikasjonens serversidekontroller.

### Application permissions

I App Registration velger du **API permissions → Add a permission → Microsoft Graph → Application permissions**. For funksjonene som brukes av prosjektet, konfigurer følgende rettigheter og velg **Grant admin consent**:

| Application permission | Bruk |
| --- | --- |
| `User.ReadBasic.All` | Lese elevens navn, UPN og ID, sammen med gruppetilgangen nedenfor |
| `GroupMember.Read.All` | Lese gruppemedlemmer og kontrollere brukeres gruppemedlemskap |
| `UserAuthMethod-TAP.ReadWrite.All` | Lese og opprette Temporary Access Pass-metoder |
| `DeviceManagementManagedDevices.Read.All` | Lese Intune-enheter, serienumre og enhetshelse |
| `DeviceManagementManagedDevices.PrivilegedOperations.All` | Sende synkroniserings- og wipe-forespørsler |
| `BitlockerKey.Read.All` | Lese BitLocker-metadata og selve gjenopprettingsnøkkelen |

Alle rettighetene i tabellen krever administratorconsent. `UserAuthenticationMethod.ReadWrite.All` er et bredere alternativ for TAP hvis den avgrensede TAP-rettigheten ikke er tilgjengelig i miljøet. Ikke legg til begge uten behov. Tilsvarende er ikke `DeviceManagementManagedDevices.ReadWrite.All` nødvendig bare for å lese enhetsdata, og `BitlockerKey.ReadBasic.All` er ikke tilstrekkelig for å hente selve nøkkelverdien.

Listen bygger på Microsofts dokumentasjon for [gruppemedlemmer](https://learn.microsoft.com/en-us/graph/api/group-list-members?view=graph-rest-1.0), [medlemskapskontroll](https://learn.microsoft.com/en-us/graph/api/directoryobject-checkmembergroups?view=graph-rest-1.0), [TAP](https://learn.microsoft.com/en-us/graph/api/authentication-post-temporaryaccesspassmethods?view=graph-rest-1.0), [enhetslesing](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-list?view=graph-rest-1.0), [synkronisering](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-syncdevice?view=graph-rest-1.0), [wipe](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-wipe?view=graph-rest-1.0), [BitLocker](https://learn.microsoft.com/en-us/graph/api/bitlockerrecoverykey-get?view=graph-rest-1.0) og [rettighetsreferansen](https://learn.microsoft.com/en-us/graph/permissions-reference).

Oppsettet forutsetter vanlige grupper uten skjult medlemskap. Skjulte medlemskap krever særskilt vurdering og eventuell `Member.Read.Hidden`. Elev-, skole- og klasselistene leser direkte brukermedlemmer; bruk direkte medlemskap for elevene i disse gruppene.

Applikasjonsrettighetene gjelder tenantens ressurser, mens skoleavgrensningen håndheves av applikasjonen. Begrens hvem som kan bruke og administrere appen, og test oppsettet med egne testbrukere før produksjonsbruk.

## Konfigurasjon

Kopier `appsettings.example.json` til `appsettings.json` og fyll inn verdier for eget miljø:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

Følgende eksempel bruker kun generiske verdier:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "YOUR-TENANT-ID",
    "ClientId": "YOUR-CLIENT-ID",
    "ClientSecret": "YOUR-CLIENT-SECRET",
    "CallbackPath": "/signin-oidc"
  },
  "Audit": {
    "LogFilePath": "C:\\Logger\\tap-audit.log"
  },
  "Cache": {
    "GroupMembersMinutes": 60
  },
  "Groups": {
    "ITDrift": "GUID",
    "Teachers": "GUID",
    "Superbrukere": "GUID",
    "A3Students": "GUID",
    "Schools": {
      "School1": {
        "GroupId": "GUID",
        "Classes": {
          "8A": "GUID",
          "9A": "GUID",
          "9B": "GUID",
          "10A": "GUID"
        }
      },
      "School2": {
        "GroupId": "GUID",
        "Classes": {
          "8A": "GUID",
          "9A": "GUID",
          "10A": "GUID"
        }
      }
    }
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

Alle `GUID`-verdier erstattes med Object ID for riktig Entra ID-gruppe. Konfigurer alle de fire rolle-/elevgruppenøklene, inkludert `Superbrukere`.

`ClientSecret` skal behandles som en hemmelighet. Den kan også settes gjennom konfigurasjonsleverandører som miljøvariabelen `AzureAd__ClientSecret`. Begrens tilgangen til lokal konfigurasjon, og legg aldri reelle credentials i eksempelfilen.

Tilpass `AllowedHosts` til driftsmiljøet.

### Skoler og klasser

Under `Groups:Schools` har hver skole:

- Et navn som brukes i visningen.
- `GroupId`: Object ID for skolegruppen.
- `Classes`: klassenavn med Object ID for hver klassegruppe.

Legg til flere skoler og klasser ved å følge samme struktur som i eksempelfilen. Brukerne som skal ha skoletilgang, og elevene ved skolen, må ha medlemskap i de aktuelle skolegruppene.

### TAP

`TAP:OneTime:LifetimeMinutes` angir kodens levetid i minutter og må være større enn null.

`TAP:OneTime:IsUsableOnce` bestemmer om koden kan brukes én gang (`true`) eller flere ganger innenfor gyldighetstiden (`false`). Hvis innstillingen mangler, bruker koden `true`.

Verdiene må være forenlige med tenantens TAP-policy. Eksempelfilen bruker 60 minutter og engangsbruk.

### Cache

`Cache:GroupMembersMinutes` angir hvor lenge gruppemedlemslister caches i minnet. Standardverdien er 60 minutter.

Endringer i medlemskap kan derfor bruke tid på å vises i elevoversikten. Serversidekontrollene ved administrative handlinger bruker egne medlemskapsoppslag.

### Audit-fil

`Audit:LogFilePath` angir plasseringen av den lokale auditfilen, for eksempel `C:\Logger\tap-audit.log`. Plasser filen utenfor webroot.

Opprett katalogen og gi applikasjonens kjøreidentitet nødvendig lese- og skrivetilgang. Ved IIS-drift vil dette normalt være identiteten til den aktuelle Application Pool-en. Begrens øvrig tilgang til loggfilen.

## Auditlogg

Loggen kan inneholde tidspunkt, operatør, elev, skole/klasse, handling, resultat, IP-adresse, serienummer, Intune-enhets-ID og Entra-enhets-ID.

Den samlede audit-siden er beskyttet av IT-drift-policyen og viser inntil 500 av de siste hendelsene. Elevdetaljsiden har også handlingshistorikk for den aktuelle eleven.

Loggen inneholder person- og enhetsidentifikatorer og må behandles som sensitiv driftsdata. Etabler passende tilgangsbegrensning, oppbevaring og sletting.

**Selve TAP-koden og BitLocker-nøkkelverdien lagres ikke i auditloggen.**

Loggen kan være ufullstendig ved drifts-, skrive- eller konfigurasjonsfeil. For wipe og synkronisering dokumenterer en sendt forespørsel ikke at enheten har fullført handlingen.

## Bygg og lokal kjøring

Når lokal konfigurasjon og App Registration er satt opp:

```powershell
dotnet restore
dotnet build -c Release
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

Åpne `https://localhost:7290`. Registrer også `https://localhost:7290/signin-oidc` som **Web redirect URI** i App Registration for lokal utvikling. HTTPS-profilen i `Properties/launchSettings.json` setter miljøet til `Development` kun ved lokal kjøring.

Bruk en separat testkonfigurasjon og testdata ved utprøving. Lokal kjøring kan utføre reelle Graph-operasjoner dersom den konfigureres mot en tenant; dette er ikke en simulering.

## Publisering til IIS

Bygg publiseringsfilene til en separat mappe:

```powershell
dotnet publish -c Release -o C:\Publish\Elevtilgang
```

1. Installer passende ASP.NET Core Hosting Bundle på IIS-serveren.
2. Opprett nettsted og Application Pool for applikasjonen. Bruk normalt «No Managed Code».
3. Sett opp HTTPS-binding og et gyldig sertifikat.
4. Plasser miljøkonfigurasjon sikkert på serveren, og gi kjøreidentiteten nødvendige filrettigheter.
5. Opprett auditkatalog utenfor webroot og gi nødvendige rettigheter.
6. Kontroller at redirect URI i App Registration samsvarer med nettstedets adresse.
7. Kontroller at driftsmiljøet er `Production`. Prosjektfilen setter ikke `Development` ved publisering; standardmiljøet er `Production` når miljøvariablene ikke overstyrer det. Kontroller også generert `web.config` og serverens miljøvariabler.
8. Stopp applikasjonen kontrollert ved utskifting av publiserte filer, og start den igjen etter oppdateringen.

Bevar serverens lokale konfigurasjon ved oppdateringer. Ikke kopier produksjonskonfigurasjon tilbake til repositoryet.

### IIS-applikasjon under en understi

Løsningen kan også konfigureres som en IIS-applikasjon under for eksempel `https://portal.example.no/elevtilgang/`. Konverter mappen til en egen IIS-applikasjon med egen Application Pool, og bruk Web redirect URI `https://portal.example.no/elevtilgang/signin-oidc`. `AzureAd:CallbackPath` skal fortsatt være `/signin-oidc`; IIS-applikasjonens basissti legges til av verten.

Handlingsadresser genereres av Razor, slik at TAP, synkronisering og BitLocker følger applikasjonens basissti. Ved annen reverse proxy må tilsvarende basissti og HTTPS-videresending konfigureres på verten; å bare legge til et prefiks i proxyen er ikke tilstrekkelig.

## Validering og ansvar

Graph-operasjonene må integrasjonstestes i eget testmiljø. Et vellykket bygg bekrefter ikke Graph-rettigheter, tilknytning til enheter eller at Intune fullfører forespurte handlinger.

Før produksjonsbruk må oppsett og funksjoner verifiseres i eget miljø, særlig gruppemedlemskap, skoleavgrensning, Graph-rettigheter, TAP-policy, enhetskobling, BitLocker-oppslag og håndtering av wipe.

Bruk av løsningen skjer på eget ansvar. Se [LICENSE](LICENSE) for prosjektets lisens.
