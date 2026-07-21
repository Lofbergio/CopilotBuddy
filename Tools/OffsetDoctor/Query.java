// Query addresses in the analyzed client: references (including the indirect ones a raw
// immediate-scan cannot see) and decompiled C for code addresses.
// usage: -postScript Query.java 0xD3F4E4 0x7FD630 ...
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class Query extends GhidraScript {

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);

        for (String a : args) {
            long v = Long.decode(a);
            Address addr = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(v);
            println("################ " + a + " ################");

            Function f = getFunctionContaining(addr);
            if (f != null) {
                println("in function: " + f.getName() + " @ " + f.getEntryPoint()
                        + (f.getEntryPoint().equals(addr) ? "  (IS the entry)" : "  (MID-FUNCTION)"));
            } else {
                println("not inside any function (data or unanalyzed)");
            }

            int n = 0;
            ReferenceIterator it = currentProgram.getReferenceManager().getReferencesTo(addr);
            while (it.hasNext() && n < 40) {
                Reference r = it.next();
                Function rf = getFunctionContaining(r.getFromAddress());
                println("  xref " + r.getReferenceType() + " from " + r.getFromAddress()
                        + (rf != null ? "  [" + rf.getName() + "]" : ""));
                n++;
            }
            println("  total refs shown: " + n);

            if (f != null && f.getEntryPoint().equals(addr)) {
                DecompileResults res = dec.decompileFunction(f, 60, monitor);
                if (res != null && res.decompileCompleted()) {
                    println("---- decompiled ----");
                    println(res.getDecompiledFunction().getC());
                } else {
                    println("  (decompile failed)");
                }
            }
            println("");
        }
        dec.dispose();
    }
}
