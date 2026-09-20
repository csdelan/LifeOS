import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Placeholder from "@tiptap/extension-placeholder";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export function JournalEditor({
  onAppend,
  disabled,
}: {
  onAppend: (html: string) => void;
  disabled?: boolean;
}) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        link: { openOnClick: false },
      }),
      Placeholder.configure({
        placeholder: "Headings, lists, bold, italic, links. A correction is a new entry.",
      }),
    ],
    content: "",
    shouldRerenderOnTransaction: true,
    editorProps: {
      attributes: {
        class: "journal-prose tiptap min-h-32 px-1 py-1 focus:outline-none",
      },
    },
  });

  if (!editor) return null;

  const empty = editor.isEmpty;

  return (
    <div className="rounded-lg border bg-background">
      <div className="flex flex-wrap gap-0.5 border-b px-1 py-1">
        <MarkBtn
          label="Bold"
          active={editor.isActive("bold")}
          onClick={() => editor.chain().focus().toggleBold().run()}
        >
          B
        </MarkBtn>
        <MarkBtn
          label="Italic"
          active={editor.isActive("italic")}
          onClick={() => editor.chain().focus().toggleItalic().run()}
        >
          <span className="italic">I</span>
        </MarkBtn>
        <MarkBtn
          label="Heading"
          active={editor.isActive("heading", { level: 2 })}
          onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
        >
          H2
        </MarkBtn>
        <MarkBtn
          label="Subheading"
          active={editor.isActive("heading", { level: 3 })}
          onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
        >
          H3
        </MarkBtn>
        <MarkBtn
          label="Bullet list"
          active={editor.isActive("bulletList")}
          onClick={() => editor.chain().focus().toggleBulletList().run()}
        >
          •
        </MarkBtn>
        <MarkBtn
          label="Numbered list"
          active={editor.isActive("orderedList")}
          onClick={() => editor.chain().focus().toggleOrderedList().run()}
        >
          1.
        </MarkBtn>
        <MarkBtn
          label="Link"
          active={editor.isActive("link")}
          onClick={() => {
            const prev = editor.getAttributes("link").href as string | undefined;
            const href = window.prompt("Link URL", prev ?? "https://");
            if (href === null) return;
            if (!href) {
              editor.chain().focus().extendMarkRange("link").unsetLink().run();
              return;
            }
            editor.chain().focus().extendMarkRange("link").setLink({ href }).run();
          }}
        >
          Link
        </MarkBtn>
      </div>
      <EditorContent editor={editor} />
      <div className="flex justify-end border-t px-2 py-2">
        <Button
          size="sm"
          disabled={disabled || empty}
          onClick={() => {
            const html = editor.getHTML();
            if (editor.isEmpty) return;
            onAppend(html);
            editor.commands.clearContent();
          }}
        >
          Append
        </Button>
      </div>
    </div>
  );
}

function MarkBtn({
  children,
  label,
  active,
  onClick,
}: {
  children: React.ReactNode;
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      className={cn(
        "rounded-md px-1.5 py-0.5 text-xs font-medium",
        active ? "bg-primary/12 text-primary" : "text-muted-foreground hover:bg-muted",
      )}
    >
      {children}
    </button>
  );
}

export function looksLikeHtml(content: string): boolean {
  return /<\/?[a-z][\s\S]*>/i.test(content);
}
