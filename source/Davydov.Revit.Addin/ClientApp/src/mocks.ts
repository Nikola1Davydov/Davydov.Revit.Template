import { registerMocks } from "revit-bridge";

// Answers of the add-in commands for `npm run dev` in a normal browser, without Revit. Inside Revit they are ignored.

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

let logSequence = 0;
const levels = ["Debug", "Information", "Information", "Warning"] as const;

registerMocks({
  "Host.GetCommands": () => [
    { name: "CountElementsByCategory", description: "Counts the model elements by category.", requestType: null, responseType: "IReadOnlyList`1" },
    { name: "GetActiveDocument", description: "Returns the active document.", requestType: null, responseType: "DocumentSummary" },
    { name: "GetLogEntries", description: "Returns the log records after the given sequence number.", requestType: "GetLogEntriesRequest", responseType: "LogPage" },
    { name: "Host.GetCommands", description: "Lists the registered commands.", requestType: null, responseType: "IReadOnlyList`1" },
  ],

  // A few new records on every poll, as a working add-in would write them
  GetLogEntries: (payload) => {
    const after = (payload as { afterSequence: number }).afterSequence;
    const entries = [];
    for (let count = Math.random() < 0.5 ? 1 : 0; count > 0; count--) {
      logSequence++;
      entries.push({
        sequence: logSequence,
        timestamp: new Date().toISOString(),
        level: levels[logSequence % levels.length],
        category: "Davydov.Revit.Addin.Features.Sample.CountElementsByCategory",
        message: `Mock record ${logSequence}`,
        exception: logSequence % 7 === 0 ? "System.InvalidOperationException: mock\n   at Mock.Run()" : null,
      });
    }
    return { entries: entries.filter((entry) => entry.sequence > after), lastSequence: logSequence };
  },

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
