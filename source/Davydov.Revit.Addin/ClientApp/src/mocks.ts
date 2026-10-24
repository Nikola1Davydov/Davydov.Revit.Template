import { registerMocks } from "revit-bridge";

// Answers of the add-in commands for `npm run dev` in a normal browser, without Revit. Inside Revit they are ignored.

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

registerMocks({
  GetActiveDocument: () => ({ key: "C:\\Models\\Sample.rvt", title: "Sample.rvt" }),

  CountElementsByCategory: async (_, { onProgress, signal }) => {
    for (let step = 1; step <= 10; step++) {
      await delay(150);
      if (signal?.aborted) throw new DOMException("The call was cancelled.", "AbortError");
      onProgress?.({ fraction: step / 10, message: `${step * 50} / 500` });
    }

    return [
      { category: "Walls", count: 210 },
      { category: "Doors", count: 140 },
      { category: "Windows", count: 150 },
    ];
  },
});
