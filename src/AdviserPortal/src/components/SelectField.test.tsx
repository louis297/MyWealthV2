import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { pickOption } from "@/test/chooseOption";

function FruitField() {
  const [value, setValue] = useState("");
  return (
    <>
      <SelectField id="fruit" label="Fruit" value={value} onChange={(event) => setValue(event.target.value)}>
        <SelectItem value="">Select a fruit</SelectItem>
        <SelectItem value="apple">Apple</SelectItem>
        <SelectItem value="pear">Pear</SelectItem>
      </SelectField>
      <output>{value}</output>
    </>
  );
}

describe("SelectField", () => {
  it("chooses from a shadcn list instead of a native select", async () => {
    render(<FruitField />);

    expect(document.querySelector("select")).toBeNull();
    expect(document.querySelector("option")).toBeNull();

    fireEvent.click(screen.getByLabelText("Fruit"));
    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent?.trim())).toEqual(["Select a fruit", "Apple", "Pear"]);

    await pickOption("Apple");

    expect(screen.getByText("apple", { selector: "output" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Fruit" })).toHaveTextContent("Apple");
    expect(screen.getByRole("combobox", { name: "Fruit" }).textContent).not.toContain("▼");
    expect(document.querySelector("select")).toBeNull();
  });
});
