import { fireEvent, screen, type Screen } from "@testing-library/react";

type LabelScope = { getByLabelText: Screen["getByLabelText"] };

export async function pickOption(optionName: string) {
  const option = await screen.findByRole("option", { name: optionName });
  fireEvent.pointerDown(option, { pointerType: "mouse", button: 0 });
  fireEvent.click(option);
}

async function trigger(label: string, scope: LabelScope) {
  if (scope === screen) {
    return screen.findByLabelText(label);
  }
  return scope.getByLabelText(label);
}

export async function chooseOption(label: string, optionName: string, scope: LabelScope = screen) {
  fireEvent.click(await trigger(label, scope));
  await pickOption(optionName);
}

export async function openSelect(label: string, scope: LabelScope = screen) {
  fireEvent.click(await trigger(label, scope));
  await screen.findByRole("listbox");
}
