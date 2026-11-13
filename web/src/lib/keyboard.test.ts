import { describe, expect, it } from "vitest";
import { keyBelongsToFocus } from "./keyboard";

function on(html: string, selector: string) {
  document.body.innerHTML = html;
  return document.body.querySelector(selector)!;
}

describe("keyBelongsToFocus", () => {
  it("leaves Enter on a button or link to the browser", () => {
    const button = on("<button>Promote</button>", "button");
    expect(keyBelongsToFocus({ key: "Enter", target: button, defaultPrevented: false })).toBe(true);
    const link = on('<a href="/tasks">Tasks</a>', "a");
    expect(keyBelongsToFocus({ key: "Enter", target: link, defaultPrevented: false })).toBe(true);
  });

  it("leaves every key inside a field or an open popup alone", () => {
    const input = on("<input />", "input");
    expect(keyBelongsToFocus({ key: "ArrowDown", target: input, defaultPrevented: false })).toBe(true);
    const option = on('<div role="listbox"><div role="option" tabindex="0">Task</div></div>', '[role="option"]');
    expect(keyBelongsToFocus({ key: "ArrowDown", target: option, defaultPrevented: false })).toBe(true);
    const inDialog = on('<div role="alertdialog"><p tabindex="0">Drop?</p></div>', "p");
    expect(keyBelongsToFocus({ key: "Enter", target: inDialog, defaultPrevented: false })).toBe(true);
  });

  it("lets the page have arrows on a button, and keys on the page itself", () => {
    const button = on("<button>Dismiss</button>", "button");
    expect(keyBelongsToFocus({ key: "ArrowDown", target: button, defaultPrevented: false })).toBe(false);
    expect(keyBelongsToFocus({ key: "Enter", target: document.body, defaultPrevented: false })).toBe(false);
  });

  it("leaves a key something already handled", () => {
    expect(keyBelongsToFocus({ key: "Enter", target: document.body, defaultPrevented: true })).toBe(true);
  });
});
