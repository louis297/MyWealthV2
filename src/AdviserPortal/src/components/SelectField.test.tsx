import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SelectField } from "@/components/portal";

describe("SelectField", () => {
  it("uses a shadcn list instead of a native select", () => {
    render(
      <SelectField id="fruit" label="Fruit" value="apple" onChange={() => undefined}>
        <option value="">Select a fruit</option>
        <option value="apple">Apple</option>
      </SelectField>,
    );

    expect(document.querySelector("select")).toBeNull();
    expect(document.querySelector("option")).toBeNull();
    expect(screen.getByRole("combobox", { name: "Fruit" })).toBeInTheDocument();
  });
});
