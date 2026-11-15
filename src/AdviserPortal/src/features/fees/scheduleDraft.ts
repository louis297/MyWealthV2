import type { FeeBand } from "@/features/fees/feesApi";

/** The create / edit form as typed. Numbers stay strings until the body is built. */
export type DraftBand = { key: string; from: string; ratePercent: string };

export type ScheduleDraft = {
  quoteCurrency: string;
  effectiveFrom: string;
  minimum: string;
  cap: string;
  bands: DraftBand[];
};

/** Field errors keyed like the server's `errors` (R5 field names), lower-cased: `bands[1].from`. */
export type FieldErrors = Record<string, string[]>;

export const maxBands = 10;
const rateDecimalPlaces = 4;

const decimalPattern = /^\d+(?:\.(\d+))?$/;

/** A non-negative decimal with at most `places` decimals (any number of decimals when `places` is unknown). */
function isAmount(text: string, places: number | undefined) {
  const match = decimalPattern.exec(text.trim());
  if (!match) {
    return false;
  }
  return places === undefined || (match[1]?.length ?? 0) <= places;
}

function placesText(places: number | undefined) {
  return places === undefined ? "" : ` with at most ${places} decimals`;
}

export type DraftCheck = {
  /** The quote currency's `DecimalPlaces`, when known. */
  decimalPlaces: number | undefined;
  /** Earliest allowed `effectiveFrom` (`yyyy-MM-dd`). */
  earliest: string;
  /** Report empty required fields too (after a submit attempt, and for the preview). */
  required: boolean;
  /** The preview body has no `effectiveFrom`. */
  effectiveFrom: boolean;
};

/** The form's own checks (adviser-portal-fees "Create / edit form"; R5 shape). */
export function draftErrors(draft: ScheduleDraft, check: DraftCheck): FieldErrors {
  const errors: FieldErrors = {};
  const add = (key: string, message: string) => {
    errors[key] = [...(errors[key] ?? []), message];
  };
  const places = check.decimalPlaces;

  if (check.required && draft.quoteCurrency === "") {
    add("quotecurrency", "Choose a quote currency.");
  }

  if (check.effectiveFrom) {
    if (draft.effectiveFrom === "") {
      if (check.required) {
        add("effectivefrom", "Choose the first day the schedule applies.");
      }
    } else if (draft.effectiveFrom < check.earliest) {
      add("effectivefrom", "Choose a date from tomorrow (UTC).");
    }
  }

  const minimumValid = isAmount(draft.minimum, places);
  if (draft.minimum.trim() === "") {
    if (check.required) {
      add("minimum", "Enter a minimum. Use 0 for none.");
    }
  } else if (!minimumValid) {
    add("minimum", `Minimum must be at least 0${placesText(places)}.`);
  }

  if (draft.cap.trim() !== "") {
    if (!isAmount(draft.cap, places) || Number(draft.cap) <= 0) {
      add("cap", `Cap must be above 0${placesText(places)}.`);
    } else if (minimumValid && Number(draft.cap) < Number(draft.minimum)) {
      add("cap", "The cap must be at least the minimum.");
    }
  }

  if (draft.bands.length > maxBands) {
    add("bands", `A schedule can have at most ${maxBands} bands.`);
  }
  draft.bands.forEach((band, index) => {
    const from = `bands[${index}].from`;
    if (index > 0) {
      const previous = draft.bands[index - 1];
      if (band.from.trim() === "") {
        if (check.required) {
          add(from, "Enter where the band starts.");
        }
      } else if (!isAmount(band.from, places)) {
        add(from, `A band start must be a number${placesText(places)}.`);
      } else if (isAmount(previous.from, undefined) && Number(band.from) <= Number(previous.from)) {
        add(from, "Each band must start above the one before it.");
      }
    }

    const rate = `bands[${index}].ratepercent`;
    if (band.ratePercent.trim() === "") {
      if (check.required) {
        add(rate, "Enter a rate.");
      }
    } else if (!isAmount(band.ratePercent, rateDecimalPlaces) || Number(band.ratePercent) > 100) {
      add(rate, `The rate must be between 0 and 100 with at most ${rateDecimalPlaces} decimals.`);
    }
  });

  return errors;
}

export function hasErrors(errors: FieldErrors) {
  return Object.keys(errors).length > 0;
}

export type ScheduleValues = {
  quoteCurrency: string;
  effectiveFrom: string;
  minimum: number;
  cap: number | null;
  bands: FeeBand[];
};

/** Call only on a draft that passed `draftErrors`. An empty cap sends `null`. */
export function draftValues(draft: ScheduleDraft): ScheduleValues {
  return {
    quoteCurrency: draft.quoteCurrency,
    effectiveFrom: draft.effectiveFrom,
    minimum: Number(draft.minimum),
    cap: draft.cap.trim() === "" ? null : Number(draft.cap),
    bands: draft.bands.map((band) => ({ from: Number(band.from), ratePercent: Number(band.ratePercent) })),
  };
}

const fieldKey = /^(quotecurrency|effectivefrom|minimum|cap|bands|bands\[\d+\]\.(from|ratepercent)|amounts|amounts\[\d+\])$/;

/**
 * Splits a failed call's problem into field errors (lower-cased keys) and the rest, which the page
 * shows above the form.
 */
export function problemErrors(error: unknown): { fields: FieldErrors; general: string[] } {
  const fields: FieldErrors = {};
  const general: string[] = [];
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return { fields, general };
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  for (const [key, messages] of Object.entries(data?.errors ?? {})) {
    const normalized = key.toLowerCase();
    if (fieldKey.test(normalized)) {
      fields[normalized] = [...(fields[normalized] ?? []), ...messages];
    } else {
      general.push(...messages);
    }
  }
  if (!data?.errors && data?.detail) {
    general.push(data.detail);
  }
  return { fields, general };
}
