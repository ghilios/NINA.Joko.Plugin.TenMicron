grammar Time;

// Accepted mount response formats:
//   HH:MM.M#       (low precision: tenth-of-minute, '.' separator)
//   HH:MM:SS#      (high precision: integer seconds)
//   HH:MM:SS.S#    (ultra precision: tenth-of-second)
//   HH:MM:SS.SS#   (ultra precision: hundredth-of-second)
//
// The previous grammar used four alternatives where the trailing integer field
// carried different semantic names (tenth_minutes / seconds / tenth_seconds /
// hundredth_seconds) but the same lexer rule. ANTLR picked the first matching
// alternative and silently mis-classified inputs. The grammar now uses a single
// `fractional_seconds` rule, and the parser disambiguates tenth-vs-hundredth by
// the length of the captured text.

time  :  hours ':' minutes '.' tenth_minutes '#'
      | hours ':' minutes ':' seconds '.' fractional_seconds '#'
      | hours ':' minutes ':' seconds '#'
      ;

hours : INTEGER ;
minutes : INTEGER ;
seconds : INTEGER ;
tenth_minutes : INTEGER ;
fractional_seconds : INTEGER ;

fragment DIGIT : [0-9] ;
INTEGER : DIGIT+ ;
