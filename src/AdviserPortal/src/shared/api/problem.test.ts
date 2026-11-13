import { describe, expect, it } from "vitest";
import { problemCode } from "@/shared/api/problem";

describe("problemCode", () => {
  it("reads the code of a problem body", () => {
    expect(problemCode({ status: 409, data: { code: "portfolio_shared", detail: "Shared." } })).toBe("portfolio_shared");
  });

  it("is undefined when the body has no string code", () => {
    expect(problemCode({ status: 409, data: { title: "Conflict" } })).toBeUndefined();
    expect(problemCode({ status: 409, data: { code: 7 } })).toBeUndefined();
    expect(problemCode({ status: 409, data: "" })).toBeUndefined();
    expect(problemCode({ status: 409 })).toBeUndefined();
  });

  it("is undefined for something that is not an error object", () => {
    expect(problemCode(undefined)).toBeUndefined();
    expect(problemCode(null)).toBeUndefined();
    expect(problemCode("409")).toBeUndefined();
  });
});
