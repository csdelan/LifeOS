import { useId, useMemo, useRef, useState } from "react";
import { XIcon } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Popover, PopoverAnchor, PopoverContent } from "@/components/ui/popover";
import { useTagUniverse } from "@/lib/queries";
import { splitTags, suggestTags } from "@/lib/tag-suggest";
import { cn } from "@/lib/utils";

/**
 * A tag text box that autocompletes from the tags already in use (GEN-1), so the
 * same tag is reused instead of retyped with a variation. Focus shows the most-used
 * tags; typing narrows them. ↑/↓ move through the list, Enter or a click picks one.
 *
 * By default a pick, Enter on typed text, or a comma commits through `onCommit` (a
 * new tag is still allowed). With `fillOnPick` a pick only fills the box and Enter is
 * left to the surrounding form — for fields that edit a single name, like rename.
 */
export function TagInput({
  value,
  onValueChange,
  onCommit,
  exclude = [],
  fillOnPick = false,
  commitOnBlur = false,
  placeholder = "Add a tag",
  id,
  disabled,
  autoFocus,
  className,
  "aria-label": ariaLabel,
}: {
  value: string;
  onValueChange: (value: string) => void;
  /** Normalized tags to add (typed text may hold several, comma-separated). */
  onCommit?: (tags: string[]) => void;
  /** Tags already on the item; they are not offered again. */
  exclude?: readonly string[];
  fillOnPick?: boolean;
  /** Commit leftover typed text when focus leaves, so a form's Save does not drop it. */
  commitOnBlur?: boolean;
  placeholder?: string;
  id?: string;
  disabled?: boolean;
  autoFocus?: boolean;
  className?: string;
  "aria-label"?: string;
}) {
  const { data: universe } = useTagUniverse();
  const inputRef = useRef<HTMLInputElement>(null);
  const [focused, setFocused] = useState(false);
  const [dismissed, setDismissed] = useState(false);
  const [active, setActive] = useState(-1);
  const listId = useId();

  const suggestions = useMemo(
    () => suggestTags(universe ?? [], value, exclude),
    [universe, value, exclude],
  );
  const open = focused && !dismissed && !disabled && suggestions.length > 0;
  const activeIndex = open && active < suggestions.length ? active : -1;

  function pick(tag: string) {
    setActive(-1);
    if (fillOnPick) {
      onValueChange(tag);
      setDismissed(true);
      return;
    }
    onCommit?.([tag]);
  }

  function commitTyped() {
    const tags = splitTags(value);
    if (tags.length > 0) onCommit?.(tags);
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    const n = suggestions.length;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      if (n === 0) return;
      e.preventDefault();
      if (!open) {
        setDismissed(false);
        setActive(e.key === "ArrowDown" ? 0 : n - 1);
        return;
      }
      setActive((a) =>
        e.key === "ArrowDown" ? (a + 1) % n : a <= 0 ? n - 1 : a - 1,
      );
      return;
    }
    if (e.key === "Enter") {
      if (activeIndex >= 0) {
        e.preventDefault();
        pick(suggestions[activeIndex].tag);
        return;
      }
      if (!fillOnPick) {
        e.preventDefault();
        commitTyped();
      }
      return;
    }
    if (e.key === "," && !fillOnPick) {
      e.preventDefault();
      commitTyped();
      return;
    }
    if (e.key === "Escape" && open) {
      setDismissed(true);
      setActive(-1);
    }
  }

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (!next) setDismissed(true);
      }}
    >
      <PopoverAnchor asChild>
        <Input
          ref={inputRef}
          id={id}
          value={value}
          disabled={disabled}
          autoFocus={autoFocus}
          placeholder={placeholder}
          aria-label={ariaLabel}
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={open ? listId : undefined}
          aria-activedescendant={activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined}
          autoComplete="off"
          className={className}
          onChange={(e) => {
            onValueChange(e.target.value);
            setDismissed(false);
            setActive(-1);
          }}
          onFocus={() => {
            setFocused(true);
            setDismissed(false);
          }}
          onBlur={() => {
            setFocused(false);
            setActive(-1);
            if (commitOnBlur && !fillOnPick) commitTyped();
          }}
          onKeyDown={onKeyDown}
        />
      </PopoverAnchor>
      <PopoverContent
        id={listId}
        role="listbox"
        aria-label="Existing tags"
        align="start"
        className="max-h-64 w-(--radix-popover-trigger-width) min-w-48 gap-0 overflow-y-auto p-1"
        onOpenAutoFocus={(e) => e.preventDefault()}
        onCloseAutoFocus={(e) => e.preventDefault()}
        onInteractOutside={(e) => {
          if (inputRef.current?.contains(e.target as Node)) e.preventDefault();
        }}
      >
        {suggestions.map((s, i) => (
          <div
            key={s.tag}
            id={`${listId}-${i}`}
            role="option"
            aria-selected={i === activeIndex}
            className={cn(
              "flex cursor-default items-center justify-between gap-2 rounded-md px-2 py-1.5 text-sm",
              i === activeIndex ? "bg-muted text-foreground" : "text-foreground/90",
            )}
            // Keep focus in the text box so typing can continue after a pick.
            onMouseDown={(e) => e.preventDefault()}
            onMouseEnter={() => setActive(i)}
            onClick={() => pick(s.tag)}
          >
            <span className="truncate">
              <span className="text-muted-foreground">#</span>
              {s.tag}
            </span>
            <span className="shrink-0 text-[0.6875rem] text-muted-foreground tabular-nums">
              {s.itemCount}
            </span>
          </div>
        ))}
      </PopoverContent>
    </Popover>
  );
}

/**
 * A comma-separated tag list edited as chips plus an autocompleting box — for forms
 * that collect tags before the item exists (New). The value stays a comma-joined string.
 */
export function TagListField({
  value,
  onChange,
  id,
  placeholder = "Add a tag",
}: {
  value: string;
  onChange: (value: string) => void;
  id?: string;
  placeholder?: string;
}) {
  const tags = splitTags(value);
  const [draft, setDraft] = useState("");

  return (
    <div className="space-y-2">
      {tags.length > 0 ? (
        <ul className="flex flex-wrap gap-1.5" aria-label="Tags to add">
          {tags.map((t) => (
            <li key={t}>
              <span className="inline-flex h-6 items-center gap-1 rounded-full border border-border bg-card px-2 text-[0.75rem] text-foreground/80">
                <span className="text-muted-foreground">#</span>
                {t}
                <button
                  type="button"
                  className="rounded-full p-1.5 text-muted-foreground hover:bg-muted hover:text-foreground"
                  aria-label={`Remove tag ${t}`}
                  onClick={() => onChange(tags.filter((x) => x !== t).join(", "))}
                >
                  <XIcon className="size-3" aria-hidden />
                </button>
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      <TagInput
        id={id}
        value={draft}
        onValueChange={setDraft}
        exclude={tags}
        placeholder={placeholder}
        commitOnBlur
        onCommit={(added) => {
          onChange([...new Set([...tags, ...added])].join(", "));
          setDraft("");
        }}
      />
    </div>
  );
}
