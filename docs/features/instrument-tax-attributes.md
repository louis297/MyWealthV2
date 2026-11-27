---
title: Tax attributes of an Instrument
status: review
phase: 2
language: en
owner: ""
created: 2026-11-27
last_updated: 2026-11-27
related:
  - ../roadmaps/fif-roadmap.md
  - ../future-plans.md
  - instruments.md
  - demo-tenant-seed.md
  - simulated-instruments.md
  - dividends.md
  - external-income.md
  - customers.md
  - currencies.md
  - ../backoffice/back-office-instruments.md
  - ../backoffice/back-office.md
  - ../fixes/tenant-composite-keys.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Tax attributes of an Instrument

Step 1 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR1** and writes decision **Q6** (who classifies) as a rule. It also answers the five points TR1 lists under "Also decide".

Today an Instrument has a symbol, a name and a quote currency. Nothing says which country its issuer is in, and nothing says how the FIF rules treat a holding of it. Every later item of the roadmap starts by asking which holdings count, so this slice comes first.

After this slice a person with `instruments.manage` states two things about an Instrument: the **issuer country**, and a **FIF treatment** per tax year. An Instrument with no treatment for a tax year is **not classified** for that year. Nothing in this slice reads the two attributes: no write, valuation, report or job changes its behaviour. The slice adds the attributes and carries them through Domain, the database, the API, the seeds and the Back Office, as [account-custody-mode.md](account-custody-mode.md) did for the custody mode.

New: route `PUT /instruments/{id}/tax-attributes`; route `GET /countries`; fields `issuerCountry` and `fifTreatments` on the instrument item; Domain enum `FifTreatment`, child entity `InstrumentFifTreatment`, constant list `Countries`, and `Instrument.SetTaxAttributes` and `Instrument.FifTreatmentFor`; script `0061_instrument_tax_attributes.sql`; a tax attributes section on the Back Office instrument page; the demo catalog classified. No new permission, problem code, domain event, job, port or setting. The simulator, the two portals and the BFFs do not change.

**Status is `review` (2026-11-27).** It is not a build contract. The points of §11 are open until they are confirmed. The changes to accepted docs listed in §12 are a plan; they are made when this spec is accepted.

The tax statements in this spec were read again from Inland Revenue's guide on 2026-11-27 (§14). The Act and Inland Revenue's lookup tool refused automated reads on the day; §14 says what follows from that. Nothing here is tax advice.

---

## 1. Summary

| | Today | After this slice |
| --- | --- | --- |
| The issuer's country | Not recorded. A dividend states its own tax source (`NewZealand` or `Foreign`) | `issuerCountry` on the Instrument: an ISO 3166-1 alpha-2 code, or none |
| How the FIF rules treat a holding | Not recorded | A FIF treatment per tax year: `NotForeign`, `ExemptAsxListed`, `ExemptOther`, `Attributing` or `AttributingNoFdr`. With none, the Instrument is not classified for that year |
| Who says so | — | A person with `instruments.manage`, by hand, per Tenant (Q6). The platform calls no outside tool |
| Where | — | `PUT /instruments/{id}/tax-attributes`, and the Back Office instrument page |
| What reads it | — | Nothing yet. TR2 to TR6 of the roadmap read it through `Instrument.FifTreatmentFor(taxYear)` |
| Existing Instruments | — | No country and not classified. The demo catalog is classified by the seeds |

The rule that distinguishes this slice: a classification is a person's statement about one Instrument and one tax year. The platform stores it and checks that it agrees with itself. It does not check it against Inland Revenue, an exchange or a company register, and it never fills one in.

### 1.1 Worked example: the demo catalog

Roadmap T12 asks for a worked example before the spec. This is the demo catalog classified by hand for tax year 2027 (1 April 2026 to 31 March 2027). 2027 is the first tax year the Development price history can open: its opening value needs the close of 31 March 2026, and the history floor date is 2026-01-01.

| Symbol | Issuer | `issuerCountry` | Treatment for 2027 | Why |
| --- | --- | --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | `US` | `Attributing` | A United States fund. A foreign unit trust is treated as a foreign company (S1 p.25) |
| AAPL | Apple Inc. | `US` | `Attributing` | A foreign company, ordinary shares, no exemption |
| MSFT | Microsoft Corporation | `US` | `Attributing` | As AAPL |
| AIA | Auckland International Airport | `NZ` | `NotForeign` | Resident in New Zealand, so not a foreign company (S1 p.24) |
| FPH | Fisher & Paykel Healthcare | `NZ` | `NotForeign` | As AIA |
| MEL | Meridian Energy | `NZ` | `NotForeign` | As AIA |
| BHP | BHP Group | `AU` | `ExemptAsxListed` | Australian resident and on the official list of ASX (S1 p.7). **To be checked in Inland Revenue's tool by a person (§11 point 9)** |
| CBA | Commonwealth Bank of Australia | `AU` | `ExemptAsxListed` | As BHP |
| WOW | Woolworths Group | `AU` | `ExemptAsxListed` | As BHP |
| SAP | SAP SE | `DE` | `Attributing` | As AAPL |
| AIR | Airbus SE | `NL` | `Attributing` | As AAPL. Registered in the Netherlands, quoted in EUR in Paris: the quote currency does not tell the country |
| OR | L'Oréal | `FR` | `Attributing` | As AAPL |
| SHEL | Shell plc | `GB` | `Attributing` | As AAPL |
| HSBA | HSBC Holdings | `GB` | `Attributing` | As AAPL |
| ULVR | Unilever | `GB` | `Attributing` | As AAPL |
| 7203 | Toyota Motor | `JP` | `Attributing` | As AAPL |
| 6758 | Sony Group | `JP` | `Attributing` | As AAPL |
| 9984 | SoftBank Group | `JP` | `Attributing` | As AAPL |

The countries come from general knowledge of each company. They were not checked against a company register on 2026-11-27, and the three ASX rows were not checked in Inland Revenue's tool, which refused automated reads (§14). Point 9 of §11 asks a person to confirm the table before this spec is accepted.

What the rows mean for other tax years (R10, R11):

| Rows of the Instrument | 2026 | 2027 | 2028 | 2029 |
| --- | --- | --- | --- | --- |
| AAPL: `2027 Attributing` | not classified | `Attributing` | `Attributing` | `Attributing` |
| BHP: `2027 ExemptAsxListed` | not classified | `ExemptAsxListed` | not classified | not classified |
| BHP after a person checks 2028 too: `2027 ExemptAsxListed`, `2028 ExemptAsxListed` | not classified | `ExemptAsxListed` | `ExemptAsxListed` | not classified |
| A company that left the ASX list: `2026 ExemptAsxListed`, `2027 Attributing` | `ExemptAsxListed` | `Attributing` | `Attributing` | `Attributing` |
| An Instrument with no row | not classified | not classified | not classified | not classified |

---

## 2. Scope

**In**

- Domain: enum `FifTreatment`; child entity `InstrumentFifTreatment`; constant list `Countries`; `Instrument.IssuerCountry`, `Instrument.FifTreatments`, `Instrument.SetTaxAttributes`, `Instrument.FifTreatmentFor`
- Script `database/schema/0061_instrument_tax_attributes.sql` and the EF mapping
- Application: command `SetInstrumentTaxAttributes`; query `GetCountries`; `GetInstruments` and `GetInstrumentById` map the two fields
- HTTP (`webapi`): `PUT /instruments/{id}/tax-attributes`; `GET /countries`; the two fields on the instrument item
- Seeds: the tax attributes of the demo catalog in `DemoCatalog`, written by the demo seed and by `TestSeed`; `TestSeed`'s two simulated instruments
- Back Office: the tax attributes section of the instrument detail page and two list columns
- Tests listed in §10

**Out**

- Any behaviour that reads the attributes: the tax year events, the cost in NZD, the threshold test, FIF income, the FIF report and the income summary (roadmap TR2 to TR6)
- Where a tax year starts and ends. This slice uses the name of a tax year only (R8); the instants in New Zealand time are TR2's (roadmap Q3)
- A call to Inland Revenue's lookup tool, to an exchange or to a company register; any check of a classification against an outside source
- A classification the platform works out, suggests or copies from another Tenant
- A check of a dividend's tax source against the issuer country (R23)
- An issuer that changes its country of tax residence: the country is one value, not a value per tax year (§11 point 2)
- A treatment that differs between two holders of the same Instrument in one tax year. A share first bought during the year in a company that joined the ASX list after the year began is such a case (roadmap §2); the row is the classifier's one statement for the year
- An Instrument that changes its treatment during a tax year (roadmap §7)
- ISIN, exchange and kind of an Instrument. They stay out, as instruments §2 says
- A platform-wide Instrument or a classification shared between Tenants
- A page for the tax attributes in the Adviser Portal or the Customer Portal. A TenantAdmin uses the API; a SystemAdmin uses the Back Office
- A list filter on the country or the treatment; a bulk write over several Instruments; a history of earlier classifications
- A countries table, a way to disable a country, country names in another language

---

## 3. Stories

1. As a SystemAdmin I open an Instrument of a Tenant in the Back Office and state its issuer country and its FIF treatment from a tax year on, so that a later tax report knows whether a holding of it counts.
2. As a TenantAdmin I do the same for my firm's catalog through the API.
3. As the person who classifies, I confirm an ASX exemption for one tax year at a time, because the exemption is tested each year. A year I have not confirmed shows as not classified, not as exempt.
4. As the person who classifies, I state "attributing" for a United States share once, and it stands for the later years until I say otherwise.
5. As an Adviser I read the country and the treatments of an Instrument, and I cannot change them (403).
6. As the operator I run the demo seed, and the 18 instruments of the demo catalog are classified without typing.
7. As a later roadmap item I ask one question, "how is this Instrument treated in tax year Y", and get one of six answers, of which "not classified" makes my result incomplete.

---

## 4. Rules

### 4.1 Access and common rules

| ID | Rule |
| --- | --- |
| R1 | **Access.** No new permission. `PUT /instruments/{id}/tax-attributes` takes `instruments.manage`, with the rules of `PUT /instruments/{id}` (instruments R1, R2, R4, R4a): missing / dead Bearer → 401, never 302; Adviser and Customer → 403; a disabled current tenant → 403; another tenant's or a missing id → 404 for a TenantAdmin; a SystemAdmin may write any tenant's Instrument by its PublicId. The two fields are read with `instruments.read` by everyone who can read the Instrument. `GET /countries` is for any authenticated caller, as `GET /currencies`: it has no policy of its own, and a missing / dead Bearer → 401. |
| R2 | **Who classifies (roadmap Q6).** A person with `instruments.manage`, by hand, per Tenant, because the catalog is the Tenant's. Two Tenants may hold the same symbol and classify it differently. The platform calls no Inland Revenue tool and no other outside source, and it never sets, suggests or changes a tax attribute outside the seeds of §4.7. |
| R3 | **Nothing reads it yet.** In this slice no write, valuation, report, job or portal page reads the tax attributes. An Instrument with no country and no treatment is created, traded, valued and reported exactly as today. |

### 4.2 Issuer country

| ID | Rule |
| --- | --- |
| R4 | **Meaning.** `issuerCountry` is the country the issuer of the Instrument is tax resident in, after any double tax agreement. For a fund or a unit trust it is the country of the fund. It is not the country of the exchange, not the quote currency, and not always the country of incorporation. It is optional: `null` means not stated. An Instrument has one issuer country, not one per tax year. |
| R5 | **Shape.** An ISO 3166-1 alpha-2 code: two letters, trimmed and stored upper case. It must be a code of `Countries` (R6), else 400 `errors.IssuerCountry`, `IssuerCountry must be an ISO 3166-1 alpha-2 country code.` So `GB` is accepted and `UK` is refused. |
| R6 | **`Countries`.** A constant list in Domain of the officially assigned ISO 3166-1 alpha-2 codes, each with its English short name, taken from the standard when this slice is built. It changes only with an amendment to this spec, as `WithholdingRates` does. There is no table, no flag and no write route. `GET /countries` returns every entry as `{ code, name }`, sorted by name, without paging. |

### 4.3 FIF treatment

| ID | Rule |
| --- | --- |
| R7 | **Values.** A FIF treatment is one of the five values of the table below. A sixth value, `NotClassified`, is the answer of R11 when no row stands for a tax year. It is never stored and never accepted in a body. |
| R8 | **Tax year.** A tax year is named by the calendar year it ends in: tax year 2027 ends on 31 March 2027 (roadmap T2, S2). It is an integer. This slice stores and compares the name only. |
| R9 | **Rows.** `fifTreatments` is a list of 0 to 50 rows `{ taxYear, treatment, note }`. `taxYear`: an integer from 2008 to the calendar year of the server's UTC date plus 1, else 400 `errors["FifTreatments[i].TaxYear"]`, `TaxYear must be between 2008 and {max}.` The lower bound is a guard against typing errors, not a statement about the law. A tax year appears at most once, else 400 `errors.FifTreatments`, `Each tax year can be entered once.` More than 50 rows → 400 `errors.FifTreatments`, `At most 50 FIF treatments can be entered.` `treatment`: one of the five values by exact name, else 400 `errors["FifTreatments[i].Treatment"]`, `Treatment must be NotForeign, ExemptAsxListed, ExemptOther, Attributing, or AttributingNoFdr.` `note`: optional text, trimmed, at most 500 characters (else 400 `errors["FifTreatments[i].Note"]`); an empty note is stored as none. With `ExemptOther` the note is required and names the exemption: else 400 `errors["FifTreatments[i].Note"]`, `Note is required for ExemptOther: name the exemption.` |
| R10 | **Which years a row stands for.** A row of `NotForeign`, `Attributing` or `AttributingNoFdr` stands for its tax year and for every later tax year, until the next row. A row of `ExemptAsxListed` or `ExemptOther` stands for its own tax year only. An exemption takes a holding out of the calculation and is tested each year (S1 p.7, S5), so a person confirms it for each year. |
| R11 | **The treatment of a tax year.** `Instrument.FifTreatmentFor(taxYear)` takes the row with the greatest `taxYear` that is not after the year asked for. No such row → `NotClassified`. The row is for the year asked for → its treatment. The row is for an earlier year → its treatment when it is not an exemption, and `NotClassified` when it is. An exemption row therefore also ends what stood before it: after `2026 Attributing`, `2027 ExemptAsxListed`, tax year 2028 is `NotClassified`, not `Attributing`. §1.1 shows the cases. |
| R12 | **Agreement with the country.** Checked on the body as a whole, because the write replaces everything (R16). With at least one row, `issuerCountry` is required: else 400 `errors.IssuerCountry`, `IssuerCountry is required when a FIF treatment is set.` With `issuerCountry` `NZ`, every row must be `NotForeign`: else 400 `errors["FifTreatments[i].Treatment"]`, `An issuer resident in New Zealand is NotForeign.` With any other country, no row may be `NotForeign`: else `NotForeign is for an issuer resident in New Zealand.` A row of `ExemptAsxListed` needs `issuerCountry` `AU`: else `ExemptAsxListed is for an issuer resident in Australia.` A country with no row is allowed. |
| R13 | **What a value states.** The table below says what the classifier asserts with each value. The platform checks R12 and nothing else. |
| R14 | **One Instrument, one interest (roadmap Q7).** Each Instrument is classified on its own. Two Instruments of one Tenant for the same company on two exchanges are two rows of the catalog and two interests, as entered. Nothing links them and nothing copies a classification between them. |
| R15 | **Existing data.** No back-fill. Every Instrument that exists when the script runs has no issuer country and no row, so it is not classified for every tax year, until a person or a seed (§4.7) says otherwise. |

The values of R7 and what each states (R13):

| Value | The classifier states | In the FIF calculation and the threshold |
| --- | --- | --- |
| `NotForeign` | The issuer is resident in New Zealand and not treated as non-resident under a double tax agreement. It is not a foreign company (S1 p.24), so a holding is not a FIF interest. A New Zealand company listed on an overseas exchange is still `NotForeign` | No |
| `ExemptAsxListed` | For this tax year the holding is a share in a company that is listed on the official list of ASX Limited, is an Australian resident and not treated as resident in another country under an agreement between Australia and that country, maintains a franking account, and is not stapled stock (S1 p.7, section EX 31). A listing on ASX alone is not enough | No. It is taxed like a New Zealand share |
| `ExemptOther` | For this tax year a holding is not an attributing interest for another reason, which the note names: for example an Australian unit trust with adequate turnover or distributions (S1 p.6, section EX 32), or a foreign debt instrument, which is not a FIF (S1 p.4) | No |
| `Attributing` | The issuer is a foreign company or a foreign unit trust, no exemption applies, and the holding is an ordinary share: the fair dividend rate method is allowed (S1 p.14) | Yes |
| `AttributingNoFdr` | As `Attributing`, but the holding is of a kind the fair dividend rate method cannot be used for: a fixed-rate foreign equity, a non-participating redeemable share, or another kind of the list in S1 p.16 | Yes. Roadmap Q12 says what the calculation does with it |
| `NotClassified` (not stored) | Nobody has said. No row stands for the tax year | The result is incomplete (roadmap T5) |

### 4.4 The write

| ID | Rule |
| --- | --- |
| R16 | **One write for all of it.** `PUT /instruments/{id}/tax-attributes` takes `{ issuerCountry, fifTreatments, rowVersion }` and replaces the issuer country and every row with what the body states. `issuerCountry` and `fifTreatments` must both be present: `issuerCountry` may be `null`, and `fifTreatments` may be an empty list. A missing one → 400 at the field. Any other field in the body → 400. A row of the body with the tax year of a stored row replaces its treatment and note; a stored row whose tax year is not in the body is deleted. Success → 204. |
| R17 | **`rowVersion`.** Required, the Instrument's own (instruments R13): missing → 400, stale → 409. A successful write always updates the Instrument row, so its `rowVersion` moves even when only a treatment row changed, and when the body equals what is stored. The rows have no `rowVersion` of their own. |
| R18 | **Any Instrument.** A disabled Instrument can be classified: it may still be held, as instruments R24 says of corporate actions. Holdings, open orders and stored reports do not block the write, because nothing reads the attributes (R3). A report that later reads them keeps its own copy of what it used (roadmap T9), so a changed classification never changes a stored report. |
| R19 | **Other writes.** `POST /instruments` takes no tax attribute: a new Instrument has no country and is not classified, and an Adviser, who may create, cannot classify. `PUT /instruments/{id}` still takes `name` and `symbol` only and refuses any other field (instruments R14). Renaming, disabling or enabling an Instrument does not change its tax attributes. |
| R20 | **No event.** `SetTaxAttributes` raises no domain event and writes no outbox message, as `Rename` does not. |

### 4.5 Reads

| ID | Rule |
| --- | --- |
| R21 | **Instrument item.** The item of `GET /instruments` and `GET /instruments/{id}` gains `issuerCountry` (the code or `null`) and `fifTreatments`: every row as `{ taxYear, treatment, note }`, sorted by `taxYear` ascending, an empty list when there is none. The item carries no treatment "for the current tax year": which tax year a day belongs to is TR2's rule (R8). There is no filter and no sort on either field. |
| R22 | **Other reads.** Every read that embeds an Instrument as `{ id, symbol, name }` (holdings, transactions, orders, corporate actions) is unchanged. A Customer has no instrument policy and reads no tax attribute in this slice. |

### 4.6 Dividends

| ID | Rule |
| --- | --- |
| R23 | **The tax source of a dividend is not checked.** A dividend states its tax source, `NewZealand` or `Foreign` (dividends R3, external-income R11). The issuer country of the Instrument is a second statement, made by another person at another time. No write checks one against the other: not a corporate action entered by a TenantAdmin, not an announcement from the feed, not a dividend entered on an External account. Existing dividends are not looked at. What a tax report does when the two disagree is decided by TR2. |

### 4.7 Seeds

| ID | Rule |
| --- | --- |
| R24 | **Demo catalog.** `DemoCatalog` gains the issuer country and one row for tax year 2027 for each of its 18 instruments, as §5.1 lists them. The demo seed writes them on an instrument it creates. On a demo catalog instrument that already exists it writes them only when the instrument has no tax attribute at all: no issuer country and no row. An instrument with either is left unchanged. This amends demo-tenant-seed R6, which today leaves an existing instrument untouched. The log line of demo-tenant-seed R12 gains the count: `instruments classified 18, already classified 0`. |
| R25 | **`TestSeed`.** `TestSeed` writes the same for `demo` and `demo2`, by the same rule. Of its Development-only extras, `ACME.SIM` gets `US` and `2027 Attributing`, `KIWI.SIM` gets `NZ` and `2027 NotForeign`, and the disabled `NZD-CASH` gets nothing and stays not classified. The two simulated issuers do not exist; their attributes are fixtures so that a Custodial account in Development holds a classified Instrument of each kind. |

### 4.8 What the attributes mean for later items

Not built in this slice. Written here so that TR2 to TR6 start from one contract.

| ID | Rule |
| --- | --- |
| R26 | **One question.** A later item asks `Instrument.FifTreatmentFor(taxYear)` and nothing else about the treatment. It does not read the rows itself and does not repeat R10 or R11. |
| R27 | **Not classified is incomplete.** `NotClassified` is never read as "not foreign", "exempt" or "attributing". A result that depends on it is incomplete (roadmap T5). |
| R28 | **What a report keeps.** A stored result keeps, per Instrument, the treatment it used, the tax year of the row it came from, the note and the issuer country (roadmap T9). |

Policies: none added. `instruments.manage` writes the tax attributes; `instruments.read` reads them.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `FifTreatment` | Enum (new) | `NotClassified = 0`, `NotForeign = 1`, `ExemptAsxListed = 2`, `ExemptOther = 3`, `Attributing = 4`, `AttributingNoFdr = 5`. `IsExemption` for 2 and 3; `IsAttributing` for 4 and 5 |
| `InstrumentFifTreatment` | Entity (new, child of `Instrument`) | `TenantId`, `InstrumentId`, `TaxYear`, `Treatment`, `Note?`, audit. Built only by `Instrument`, which passes its own `TenantId` down ([tenant-composite-keys](../fixes/tenant-composite-keys.md) Decision 4). Never `NotClassified` |
| `Countries` | Static (new) | The list of R6. `IsKnown(code)`, `All`, and the constants `NewZealand = "NZ"` and `Australia = "AU"` |
| `Instrument` | Aggregate (existing) | `IssuerCountry?`, `FifTreatments`. `SetTaxAttributes(issuerCountry, rows, latestTaxYear)`: R5, R9, R12, R16. `FifTreatmentFor(taxYear)` → `FifTreatment`: R11. `FifTreatmentRowFor(taxYear)` → the row R11 chose, or none, for R28. `Create`, `Rename`, `Disable` and `Enable` do not change |

```text
SetTaxAttributes(issuerCountry, rows, latestTaxYear)
  issuerCountry: null, or a code of Countries, stored upper case
  rows: at most 50; each tax year once, 2008..latestTaxYear; a stored treatment; note <= 500
        ExemptOther needs a note
  any row                  -> issuerCountry required
  issuerCountry = NZ       -> every row NotForeign
  issuerCountry <> NZ      -> no row NotForeign
  ExemptAsxListed          -> issuerCountry = AU
  then: replace the country; update, add and remove rows by tax year; raise nothing

FifTreatmentFor(taxYear)
  row = the row with the greatest TaxYear <= taxYear
  none                                  -> NotClassified
  row.TaxYear = taxYear                 -> row.Treatment
  row is an exemption (earlier year)    -> NotClassified
  else                                  -> row.Treatment
```

Application passes `latestTaxYear` as the calendar year of the UTC date of `TimeProvider` plus 1. The entity reads no clock.

Invariants:

- An Instrument has at most one row per tax year, and at most 50 rows.
- An Instrument with a row has an issuer country.
- An Instrument with issuer country `NZ` has only `NotForeign` rows; no other Instrument has a `NotForeign` row.
- An Instrument with an `ExemptAsxListed` row has issuer country `AU`.
- An `ExemptOther` row has a note.
- A row belongs to the Tenant of its Instrument.

Domain events: none.

[domain-model.md](../domain-model.md) §8.4 carries these types and invariants when this spec is accepted.

### 5.1 Demo catalog tax attributes

Every row is for tax year 2027. The table is that of §1.1 without the reasons.

| Symbol | `issuerCountry` | Treatment | Note |
| --- | --- | --- | --- |
| VTI, AAPL, MSFT | `US` | `Attributing` | — |
| AIA, FPH, MEL | `NZ` | `NotForeign` | — |
| BHP, CBA, WOW | `AU` | `ExemptAsxListed` | `Checked in Inland Revenue's FIF Australian listed share exemption tool on <date>.` The date is the day a person checked (§11 point 9) |
| SAP | `DE` | `Attributing` | — |
| AIR | `NL` | `Attributing` | — |
| OR | `FR` | `Attributing` | — |
| SHEL, HSBA, ULVR | `GB` | `Attributing` | — |
| 7203, 6758, 9984 | `JP` | `Attributing` | — |

The three ASX rows stand for tax year 2027 only (R10). For tax year 2028 the three Instruments are not classified until a person checks them again and adds a row. That is the rule working, not a gap in the seed.

---

## 6. Database

Forward only. Do not add EF migrations.

Script `database/schema/0061_instrument_tax_attributes.sql`:

| Table | Change | Index / FK |
| --- | --- | --- |
| `Instruments` | add `IssuerCountry char(2) NULL` | `CK_Instruments_IssuerCountry`: `NULL` or two upper-case letters `A`–`Z` |
| `InstrumentFifTreatments` | add | PK `Id`. Unique `(InstrumentId, TaxYear)`. Composite FK `(TenantId, InstrumentId)` → `Instruments (TenantId, Id)` RESTRICT. FK `TenantId` → `Tenants` RESTRICT |

### 6.1 `InstrumentFifTreatments`

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | int identity | no | Internal PK. No `PublicId`: a row is never addressed over HTTP |
| TenantId | int | no | The Instrument's Tenant ([tenant-composite-keys](../fixes/tenant-composite-keys.md)) |
| InstrumentId | int | no | |
| TaxYear | smallint | no | The calendar year the tax year ends in. CHECK 2008–2199 |
| Treatment | tinyint | no | `FifTreatment`. CHECK 1–5: `NotClassified` is never stored |
| Note | nvarchar(500) | yes | CHECK: not `NULL` when `Treatment = 3` |
| Created | datetimeoffset | no | |
| CreatedBy | nvarchar(450) | no | |
| LastModified | datetimeoffset | yes | |
| LastModifiedBy | nvarchar(450) | yes | |

No `RowVersion`: the Instrument's guards the write (R17). The composite FK uses the existing `UQ_Instruments_TenantId_Id`. EF maps the single-column relationship only, as the fix says.

The schema holds what one row can show. The agreement of the rows with the country (R12) spans two tables and is held by `Instrument.SetTaxAttributes`. The upper bound of a tax year in the schema is a fixed 2199; the moving bound of R9 is held by the Domain.

No back-fill (R15). The script writes no row: the demo catalog is classified by the seeds, not by a schema script.

[database-design.md](../database-design.md) §11.1 carries the column, the table and script `0061` when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `SetInstrumentTaxAttributes` (new) | `PUT /instruments/{id}/tax-attributes`: R1, R5, R9, R12, R16–R18. `instruments.manage`; finds the Instrument as `UpdateInstrument` does; `rowVersion`; returns nothing |
| Query | `GetCountries` (new) | `GET /countries`: R6. Authenticated, no policy |
| Query | `GetInstruments`, `GetInstrumentById` (existing) | The item gains `issuerCountry` and `fifTreatments` (R21) |
| Command | `CreateInstrument`, `UpdateInstrument`, `DisableInstrument`, `EnableInstrument` (existing) | No change (R19) |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Shapes that need no Instrument: the code, the rows, the tax year bounds, the note, the agreement with the country (R5, R9, R12) | The validator, through the same Domain rules `SetTaxAttributes` applies |
| Finding the Instrument, the tenant rules, `rowVersion` (R1, R17) | The handler |
| Replacing the rows, the invariants of §5 (R16) | `Instrument.SetTaxAttributes` |
| The treatment of a tax year (R10, R11) | `Instrument.FifTreatmentFor` |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| PUT | `/instruments/{id}/tax-attributes` | `instruments.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| GET | `/countries` | authenticated | 200 list of `{ code, name }` | 401 |
| GET | `/instruments`, `/instruments/{id}` | unchanged | the item gains `issuerCountry` and `fifTreatments` (R21) | — |
| POST | `/instruments` | unchanged | unchanged (R19) | — |
| PUT | `/instruments/{id}` | unchanged | unchanged; a tax attribute in the body → 400 (R19) | — |

`{id}` is the Instrument's `PublicId`. No `Idempotency-Key`: the PUT states the whole result, so sending it twice is harmless apart from the `rowVersion`.

### 8.1 Bodies

Classify BHP for two tax years:

```json
{
  "issuerCountry": "AU",
  "fifTreatments": [
    { "taxYear": 2027, "treatment": "ExemptAsxListed", "note": "Checked in Inland Revenue's FIF Australian listed share exemption tool on 2026-12-01." },
    { "taxYear": 2028, "treatment": "ExemptAsxListed", "note": "Checked on 2027-04-06." }
  ],
  "rowVersion": "<base64>"
}
```

Classify a United States share once:

```json
{
  "issuerCountry": "US",
  "fifTreatments": [
    { "taxYear": 2027, "treatment": "Attributing", "note": null }
  ],
  "rowVersion": "<base64>"
}
```

Take every tax attribute off again: `{ "issuerCountry": null, "fifTreatments": [], "rowVersion": "<base64>" }`.

The instrument item (fields of instruments §8.1 and R23 are unchanged):

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "symbol": "BHP",
  "name": "BHP Group",
  "quoteCurrency": "AUD",
  "isActive": true,
  "marketDataConnection": null,
  "issuerCountry": "AU",
  "fifTreatments": [
    { "taxYear": 2027, "treatment": "ExemptAsxListed", "note": "Checked in Inland Revenue's FIF Australian listed share exemption tool on 2026-12-01." }
  ],
  "rowVersion": "<base64>"
}
```

An Instrument nobody has classified has `"issuerCountry": null, "fifTreatments": []`.

`GET /countries`: `[ { "code": "AU", "name": "Australia" }, … , { "code": "NZ", "name": "New Zealand" }, … ]`.

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`) except the stale `rowVersion`.

| Case | Response |
| --- | --- |
| `issuerCountry` or `fifTreatments` missing from the body; any other field | 400 (R16) |
| `issuerCountry` not a code of the list | 400 `errors.IssuerCountry` (R5) |
| A row with a tax year outside the bounds, an unknown treatment, `NotClassified`, or a note above 500 characters | 400 at `errors["FifTreatments[i].…"]` (R9) |
| `ExemptOther` with no note | 400 `errors["FifTreatments[i].Note"]` (R9) |
| A tax year twice; more than 50 rows | 400 `errors.FifTreatments` (R9) |
| A row with no `issuerCountry` | 400 `errors.IssuerCountry` (R12) |
| `NZ` with a row that is not `NotForeign`; `NotForeign` with another country; `ExemptAsxListed` with a country that is not `AU` | 400 `errors["FifTreatments[i].Treatment"]` (R12) |
| `rowVersion` missing; stale | 400; 409, as instruments R13 |
| Adviser or Customer | 403 |
| Unknown id; another tenant's id for a TenantAdmin | 404 |

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

Page contract: [back-office-instruments.md](../backoffice/back-office-instruments.md), which gains the section "Tax attributes" and BO-I9 to BO-I12 when this spec is accepted. The Back Office calls `webapi` with Bearer, as today.

| ID | Behaviour |
| --- | --- |
| BO-I9 | The detail page `/instruments/{id}` has a section "Tax attributes" under the rename form. It shows "Issuer country", a picker filled from `GET /countries` that shows the name and the code and can be cleared, and a table "FIF treatment" with one line per row of `fifTreatments`: Tax year, Treatment and Note. "Add tax year" adds a line; each line can be removed. Under the table the page says: "A treatment that is not an exemption stands for the later tax years until the next line. An exemption stands for its own tax year only." With no line it says "Not classified". |
| BO-I10 | The Treatment options read "Not foreign (New Zealand issuer)", "Exempt: ASX-listed Australian share", "Exempt: other (name it in the note)", "Attributing interest" and "Attributing interest, FDR not allowed". The page does not fill in a treatment from the country or from the quote currency, and it offers no check against an outside tool. |
| BO-I11 | "Save tax attributes" sends `PUT /instruments/{id}/tax-attributes` with the country, every line and the `rowVersion` of the loaded item, then reloads the item. A 400 is shown at its input; `errors.FifTreatments` under the table. A 409 reloads the item and shows the short conflict message of BO-I5. The rename form and the disable / enable actions do not send a tax attribute. |
| BO-I12 | The list `/instruments` gains two columns: "Country" (the code, or empty) and "FIF treatment", which shows the row with the latest tax year as "<label> from <year>" for a treatment that stands for later years and "<label> <year>" for an exemption, or "Not classified" with no row. The create page `/instruments/new` does not change. |

Adviser Portal and Customer Portal: no change. Neither has an Instruments page, and their pickers ignore the two new fields. Neither BFF gains a prefix.

### 9.1 Seeds and hosting

- The demo seed and `TestSeed`: R24, R25.
- No new configuration, secret, container, job or queue.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Countries`: `NZ`, `AU`, `US`, `GB`, `DE`, `FR`, `NL` and `JP` are known; `UK`, `XX` and a lower-case code are not; every code is two upper-case letters and appears once; every entry has a name. `Instrument.SetTaxAttributes`: the country alone; rows with a country; every refusal of R9 and R12; a second call replaces, updates and removes rows by tax year; a row carries the Instrument's `TenantId`; no domain event. `Instrument.FifTreatmentFor`: every line of the second table of §1.1; a year before the first row; an exemption row ends what stood before it (R11); `ExemptOther` stands for one year |
| Application.FunctionalTests (`Instruments/`) | `PUT /instruments/{id}/tax-attributes`: no Bearer → 401 not 302; Adviser → 403; Customer → 403; TenantAdmin and SystemAdmin → 204 and the item shows the country and the rows sorted by tax year; every 400 of §8.2; a tax year above the server's year plus 1; missing and stale `rowVersion`; the `rowVersion` moves when only a row changed; a disabled Instrument is accepted; the empty body of §8.1 clears everything. `POST /instruments` creates an Instrument with `issuerCountry` `null` and no rows; `PUT /instruments/{id}` with `issuerCountry` → 400, and a rename keeps the tax attributes. The Adviser reads both fields on the list and the item. `GET /countries`: 401 without Bearer; every role gets the list sorted by name. Two-tenant fixture: a TenantAdmin's PUT on another tenant's Instrument → 404 and nothing changes; a SystemAdmin's PUT on it → 204; the list of one tenant never shows another tenant's rows |
| Infrastructure.IntegrationTests (`AppliedSchema/InstrumentsTaxAttributesSchemaTests.cs`) | `IssuerCountry` is nullable and refuses `nz` and `NZL`; a second row for one `(InstrumentId, TaxYear)` is refused; `Treatment` 0 and 6 are refused; `Treatment` 3 with no note is refused; a row whose `TenantId` is not its Instrument's is refused; existing Instruments have no country and no row after the script |
| Infrastructure.IntegrationTests (the demo seed and `TestSeed` tests) | A new demo tenant gets the 18 instruments with the attributes of §5.1. An existing demo instrument with no tax attribute gets them; one with a country only, or with a row, is left unchanged; a second run changes nothing. `TestSeed`: `ACME.SIM` and `KIWI.SIM` as R25, `NZD-CASH` not classified. No instrument of another tenant is touched |
| Back Office (component tests) | BO-I9 to BO-I12 |

All of `Infrastructure.IntegrationTests` runs for this slice, because it adds a script.

---

## 11. Locked in this spec

No new ADR. [ADR 0010](../adr/0010-instrument-catalog.md) stays as written: the catalog is the Tenant's, and ISIN, exchange and kind stay out. It gets a dated amendment that names the new column and table when this spec is accepted.

| Item | Lock |
| --- | --- |
| Issuer country | One ISO 3166-1 alpha-2 code on the Instrument, from a constant list in Domain; optional |
| FIF treatment | Rows per tax year with five stored values; an exemption stands for its own year, any other row until the next row; `NotClassified` is the answer when no row stands |
| Who classifies | `instruments.manage`, by hand, per Tenant. No outside tool is called |
| Agreement | A row needs a country; `NZ` ⇔ `NotForeign`; `ExemptAsxListed` needs `AU`. Nothing else is checked |
| Write | `PUT /instruments/{id}/tax-attributes` replaces the country and every row, under the Instrument's `rowVersion` |
| Reads | `issuerCountry` and `fifTreatments` on the instrument item; `GET /countries` |
| Behaviour | Nothing reads the attributes in this slice. Later items ask `Instrument.FifTreatmentFor(taxYear)` |
| Dividends | The tax source of a dividend is not checked against the issuer country |
| Seeds | The demo catalog classified for tax year 2027; an existing demo instrument is classified only when it has no tax attribute |
| Script | `0061_instrument_tax_attributes.sql` |
| Permissions, problem codes, events, jobs, ports, settings | None added |

### Points to confirm

Each point states the recommendation, which the rules above already follow, and the alternative.

1. **The country is an ISO 3166-1 alpha-2 code from a constant list in Domain** (R5, R6). Recommended: the later report groups foreign tax by country (roadmap Q17) and Inland Revenue's overseas income summary is by country, so the value must be one of a known set; free text gives `UK`, `GB` and `Britain` for one country. A list in code needs no table, flag or write route, and nothing about a country changes per Tenant. Alternative A: free text. Alternative B: a `Countries` table like `Currencies`, worth it only when a country gets an attribute of its own, such as a treaty rate.
2. **One issuer country per Instrument, not one per tax year** (R4, §2 Out). Recommended: an issuer almost never changes its country, a single field can be set before anyone classifies, and a later check against a dividend needs no tax year. The cost: an issuer that moves to or from New Zealand cannot be stated for the years before the move, because R12 holds for every row. Alternative: the country on each treatment row, which states that case and makes the country need a tax year.
3. **An exemption stands for one tax year; any other treatment stands until the next row** (R10, R11). Recommended: the ASX exemption is tested each year and Inland Revenue's tool answers per ticker and financial year, so an exemption nobody confirmed for a year must not be reported for it (roadmap T5). An attributing or a New Zealand share stays what it is for years, and typing it again each year adds work and no information. Alternative A: every row stands until the next row, which is less work and reports an exemption as long as nobody looks. Alternative B: one row per Instrument and tax year for every treatment, which is the simplest model and means every held Instrument is typed again each year.
4. **Five stored values, with `ExemptOther` and a required note** (R7, R9). Recommended: roadmap §6 lists four values and not classified, and roadmap §7 says an Instrument under another exemption is classified by hand as exempt; `ExemptOther` is that value, and its note is how the report can say "under which exemption" (roadmap §4 point 1). It also takes a foreign right that is not a FIF, such as a debt instrument. Alternative: no such value, so that an Australian unit trust under section EX 32 can only be left not classified.
5. **One route that replaces the country and every row** (R16). Recommended: the tax attributes are one small statement that a person edits as a whole, the agreement rules of R12 are checked on one body, and the Instrument's `rowVersion` guards it. Alternative A: the fields on `PUT /instruments/{id}`, which would make every rename send them. Alternative B: a row resource per tax year (`PUT` and `DELETE /instruments/{id}/fif-treatments/{taxYear}`), which needs a delete and checks R12 across requests.
6. **The country and the treatments must agree, as a 400** (R12). Recommended: the three rules follow from the definitions (a foreign company is one not resident in New Zealand; the ASX exemption needs an Australian resident), so a body that breaks one is a typing error, and a tax figure built on it would be wrong without a sign. Alternative: store what is sent and let the report flag it.
7. **The tax source of a dividend is not checked against the issuer country** (R23). Recommended: a dividend records what a payer did, and on an External account every number is the caller's (external-income R4); the feed cannot be refused; existing dividends may already disagree; and the country may not be set. The tax report is where the two statements meet, so TR2 decides whether a disagreement is a flag. Alternative A: 400 on hand entry when the Instrument has a country that disagrees. Alternative B: a warning on the two entry forms of the Adviser Portal.
8. **`GET /countries` for any authenticated caller** (R6). Recommended: the Back Office picker needs the names, the Back Office does not reference Domain, and the later report pages in the portals will need the same names. Alternative: no route; the Back Office takes the two letters in a text field and shows no name.
9. **The demo classification of §1.1 and §5.1, for tax year 2027, after a person has checked it.** Recommended: accept the table once a person has looked up BHP, CBA and WOW in Inland Revenue's tool for the year that began on 1 April 2026 and has read the fifteen countries. The tool refused automated reads on 2026-11-27 (§14), and its page does not say how it names a financial year. If the tool does not say exempt for one of the three, that row becomes `Attributing`. Alternative: seed the three as not classified and classify them by hand in the Back Office.
10. **The seeds classify an existing demo instrument that has no tax attribute** (R24). Recommended: the production demo tenant already has its 18 instruments, so a seed that only classifies what it creates would classify nothing there. The seed still never overwrites a statement a person made. Alternative: keep demo-tenant-seed R6 as it is and classify the production demo tenant by hand.
11. **A tax year from 2008 to the server's calendar year plus 1** (R9). Recommended: bounds that catch a typing error without a clock rule in the schema. On 2026-11-27 the tax year in progress is 2027, so the upper bound allows it. Alternative: no upper bound, so that a treatment can be entered for a year that has not begun.
12. **No domain event and no history of earlier classifications** (R20, §2 Out). Recommended: nothing subscribes, and a report keeps what it used (R28), so the question "what was this Instrument classified as when the report ran" is answered by the report. The audit columns say who wrote a row last. Alternative: an `InstrumentTaxAttributesChanged` event, or rows that are closed and never updated.
13. **No portal page; a TenantAdmin classifies through the API** (§2 Out, §9). Recommended: the Adviser Portal has no Instruments page at all, and the first one is a slice of its own. Until then the person who classifies in practice is a SystemAdmin in the Back Office, as for a rename today. Alternative: an Instruments page in the Adviser Portal for the TenantAdmin in this slice.

Still open, not this slice:

- Everything that reads the attributes: roadmap TR2 to TR6. TR2 decides what a tax report does with a dividend whose tax source disagrees with the issuer country (R23)
- An Instruments page in the Adviser Portal, with the tax attributes, for the TenantAdmin
- A list of the Instruments not classified for a tax year, and a way to confirm last year's exemptions for the next year in one step
- An issuer that changes its country of tax residence (point 2)
- A share first bought during the year in a company that joined the ASX list after the year began, and any other treatment that differs between two holders
- ISIN, exchange and kind (instruments §11)
- A platform-wide Instrument, or a classification shared between Tenants

### Found while writing, not changed here

- Roadmap §2 says, from S7, that the ASX listing is tested at the start of the income year, or on the day of acquisition for a share first bought during the year. The guide read for this spec (S1 p.7) lists the four conditions and gives no test date, and S7 and section EX 31 could not be read again (§14). R10 does not depend on the date: it says only that an exemption is confirmed per tax year. TR2 should read section EX 31 before it relies on the date.
- Roadmap §6 TR1 lists four working values and "not classified", and roadmap §7 adds "classified by hand as exempt" for the other exemptions. The two do not match; point 4 settles it with `ExemptOther`.
- [glossary.md](../glossary.md) "Demo seed" says "its two report grants", and the row of demo-tenant-seed in [features/README.md](README.md) says "both report grants". The seed grants three (demo-tenant-seed R8).
- [back-office-instruments.md](../backoffice/back-office-instruments.md) "Out" has the words "below below".

---

## 12. Changes to other docs

A plan while this spec is in review. Each doc is changed in place when this spec is accepted.

| Doc | Change |
| --- | --- |
| [instruments.md](instruments.md) | Intro and §1: the Instrument has tax attributes, owned by this spec. "Added by later specs": R26 (the two fields on the item, R21 here) and R27 (`PUT /instruments/{id}/tax-attributes`, and that create and rename take no tax attribute). §5: the new members and child. §6.1: `IssuerCountry`; the sentence under the table keeps ISIN, exchange and kind out. §8: the route and the item. §9.1: the `TestSeed` attributes of R25. §11: a lock row. History row |
| [demo-tenant-seed.md](demo-tenant-seed.md) | §1 and R6: the seed writes the tax attributes of the demo catalog; a new rule for an existing instrument with no tax attribute (R24 here). R12: the log line. R13: `TestSeed` extras. §5.1: the country and the 2027 treatment of each row. §10: the tests. §11: the lock row "Catalog". History row |
| [back-office-instruments.md](../backoffice/back-office-instruments.md) | Backend prerequisites: the route and `GET /countries`. "Current slice" In: the tax attributes. A section "Tax attributes" with BO-I9 to BO-I12. History row |
| [back-office.md](../backoffice/back-office.md) | The Instruments summary names the tax attributes section |
| [dividends.md](dividends.md), [external-income.md](external-income.md) | Dividends R3 and external-income R11: the tax source is not checked against the Instrument's issuer country (R23 here) |
| [api-design.md](../api-design.md) | §6 catalog: Countries. §7.5: the route, the two item fields, and `GET /countries` beside §7.2. Change log |
| [domain-model.md](../domain-model.md) | §8.4: `FifTreatment`, `InstrumentFifTreatment`, `Countries`, the two `Instrument` members and the invariants of §5 |
| [database-design.md](../database-design.md) | §11.1: `IssuerCountry`, `InstrumentFifTreatments`, script `0061`; the composite FK list; the change log |
| [ADR 0010](../adr/0010-instrument-catalog.md) | A dated amendment at the end: the issuer country and the FIF treatment rows are on the catalog; ISIN, exchange and kind stay out |
| [glossary.md](../glossary.md) | Tax year, Issuer country, FIF treatment and Not classified become accepted. "Tax source (of a dividend)" points to the issuer country as a separate statement. "Instrument" names the tax attributes |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §3 "What is foreign": the Instrument has a country and a treatment. §6 TR1: the five values. §9 row 1: accepted. §8 Q6: written as R2 here |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6 | Status: TR1 accepted |

---

## 13. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. `Countries`: the list, `IsKnown`, and the two constants (R5, R6).
2. `FifTreatment`, `InstrumentFifTreatment` and `Instrument.SetTaxAttributes`: the rows, their refusals and the agreement with the country (R9, R12, R16).
3. `Instrument.FifTreatmentFor` and `FifTreatmentRowFor` (R10, R11).
4. Script `0061_instrument_tax_attributes.sql`, the EF mapping and the schema tests (§6).
5. `PUT /instruments/{id}/tax-attributes`: access, the body, `rowVersion`, the tenant isolation; the two fields on the instrument item; create and rename unchanged (R1, R16–R19, R21).
6. `GET /countries` (R6).
7. The demo catalog's tax attributes, the demo seed and `TestSeed` (R24, R25).
8. Back Office: the tax attributes section and the two list columns (BO-I9 to BO-I12).
9. The landing commit: this spec marked landed, the roadmap row and the root README.

---

## 14. Sources

Read on 2026-11-27. Codes are those of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` | What a FIF is and is not (p.4); attributing interests (p.5); the exemptions and their sections (p.6–7); the four conditions of the ASX exemption (p.7); the fair dividend rate method needs an ordinary share (p.14) and the kinds it cannot be used for (p.16); foreign company, foreign unit trust and when a company is resident in New Zealand (p.24–25); stapled stock (p.26) | Yes, in full text |
| S5 | Inland Revenue, "Foreign investment fund Australian listed share exemption tool" (`ird.govt.nz/fif-australia-tool`, last updated 5 October 2026) | The tool takes an ASX ticker code and a financial year, and answers whether the shareholding is exempt | The page: yes. The tool itself (`myir.ird.govt.nz/tools`): refused automated reads (403). No ticker was looked up |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1 | The naming of an income year (R8) | Not read again; taken from roadmap §2 |
| S7 | Inland Revenue Tax Technical, "Technical amendments to the offshore portfolio share investment rules" | When the ASX listing is tested | Not read again; the site's search could not be used. See "Found while writing" |
| — | Income Tax Act 2007, sections EX 31, EX 32, EX 46 and YA 1 | The exemptions and the definitions in the Act's own words | Not read: `legislation.govt.nz` and NZLII refused automated reads (403) |

What follows from the two refusals: the meaning of each value in §4.3 is quoted from the guide, which is Inland Revenue's summary of the Act and not the Act. No rule of this slice computes anything from those meanings; they tell the classifier what a value states. The three ASX rows of the demo catalog are unchecked until a person runs the tool (§11 point 9).

ISO 3166-1 is the standard of the International Organization for Standardization for country codes. The list of R6 is taken from it when the slice is built; it was not read for this spec.
