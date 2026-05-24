grammar Angle;

// Accepted mount response formats:
//   ±DD*MM#              (standard precision)
//   ±DD*MM:SS#           (high precision)
//   ±DD*MM:SS.S#         (ultra precision — the format mount returns after :U2#)
//   ±DD:MM:SS.S#         (legacy long form with colons throughout)

angle :  sign degrees ':' minutes ':' seconds '.' tenth_seconds '#'
      | sign degrees '*' minutes ':' seconds '.' tenth_seconds '#'
      | sign degrees '*' minutes ':' seconds '#'
      | sign degrees '*' minutes '#'
      ;
sign : SIGN ;
degrees : INTEGER ;
minutes : INTEGER ;
seconds : INTEGER ;
tenth_seconds : INTEGER ;

fragment DIGIT : [0-9] ;
SIGN : '-'|'+' ;
INTEGER : DIGIT+ ;
